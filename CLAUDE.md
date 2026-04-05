# 概要
これはResoniteの音声専用クライアントです。
FrooxEngineをUnityの描画エンジンなしで起動し、セッションに参加して空間オーディオをリアルタイム再生するコンソールアプリケーションです。

# 開発
FrooxEngineをデコンパイルしたコードが./reso-decompileにあります。必要に応じて参照してください。

# ドキュメントの整理
新しいコマンドを実装したり、既存のコマンドを修正したりした場合は、コマンドの使い方をREADME.mdにまとめてください。

---

# 実装上のノウハウ・留意点

## アーキテクチャ上の重要な判断

### MODではなくスタンドアロンexeにした理由
ResoniteModLoader (RML) を経由するMOD形式ではなく、スタンドアロンの .NET 10.0 exe として構築しています。理由：
- MODはUnityのGameObject/MonoBehaviour基盤の上で動くため、Unity描画エンジンを避けられない
- `FrooxEngine.dll` は .NET Standard 2.1 互換であり、Unity外から直接参照可能
- スタンドアロンにすることで、Unity依存を完全に排除しCPU/メモリ使用を最小化できる

### HeadOutputDevice は `Screen` にすること
`HeadOutputDevice.Headless` を指定すると、エンジンは「画面なし・音なし・入力なしのサーバー用途」と判断し、以下を**すべてスキップ**します：
- CSCore オーディオドライバの初期化
- AudioListener の付与（＝「耳」がないので音を拾えない）
- DesktopUserRoot の構築（通常のデスクトッププレイヤーとしてのアバター構成）

**`HeadOutputDevice.Screen` を指定する**ことで、描画をしなくても「通常のデスクトップクライアント」として扱われ、音声パイプラインが全て有効になります。`useRenderer: false` は別フラグなので、Screen指定でもGUIウィンドウは立ちません。

## アセンブリ読み込みの注意点

### AssemblyResolve ハンドラの配置場所
`AppDomain.CurrentDomain.AssemblyResolve` ハンドラは **`Main` メソッド内で、FrooxEngineの型を一切参照する前に** 登録する必要があります。.NET の JIT コンパイラはメソッド単位で型を解決するため、`Main` 自体に `FrooxEngine.Engine` 等の型参照があると、ハンドラ登録前にアセンブリ解決が走ってクラッシュします。

**対策：** FrooxEngineを使う全コードを `[MethodImpl(MethodImplOptions.NoInlining)]` を付けた別メソッド (`RunEngine`) に分離し、`Main` → `RunEngine` の呼び出し構造にしています。

### 全DLLのプリロードが必須
起動時に `Assembly.LoadFrom()` でアプリケーションディレクトリ内の全 `.dll` をプリロードしています。これがないと、エンジンの型スキャナが `Awwdio`, `PhotonDust` 等の Data Model Assembly を登録できず、セッション参加時に `CompatibilityError` が発生します。

プリロードで `BadImageFormatException`（ネイティブDLL）や一部の読み込みエラーが出ますが、これらは無害なので `catch` でスキップしています。

### ネイティブDLLのPATH追加
SteamAudioの `phonon.dll` 等のネイティブDLLは `runtimes/win-x64/native/` に格納されています。.NET のデフォルトでは探索パスに含まれないため、起動直後に `Environment.SetEnvironmentVariable("PATH", ...)` で追加する必要があります。

## csproj の設定

### SpecificVersion と Private
```xml
<Reference Include="FrooxEngine">
  <HintPath>$(GamePath)FrooxEngine.dll</HintPath>
  <Private>false</Private>
  <SpecificVersion>false</SpecificVersion>
</Reference>
```
- `Private=false` — ビルド出力にResoniteのDLLをコピーしない（ゲームフォルダに既にあるものを使う）
- `SpecificVersion=false` — ビルド時にバージョン完全一致を要求しない

**注意：** `SpecificVersion=false` はビルド時のみ有効です。ランタイムのバージョン照合は `AssemblyResolve` ハンドラで対応しています。

### PostBuild のコピー先
ビルド成果物は自動的に `$(GamePath)`（Resoniteインストールフォルダ）にコピーされます。`.pdb` は除外し、`AudioClient.exe`, `AudioClient.dll`, `.deps.json`, `.runtimeconfig.json` のみがコピーされます。

## セッション互換性チェックの仕組み
エンジンはセッション参加時に以下を照合します：
1. `SystemCompatibilityHash` — 全アセンブリの型定義から算出した全体ハッシュ
2. 各 Data Model Assembly の `CompatibilityHash` — アセンブリ内の型のフィールド・メソッド構造のMD5ハッシュ

**同じバージョンのResonite**を使っている限り、これらは一致します。異なるバージョン間では参加できません（これはResoniteの仕様です）。

## デバッグのヒント

### CSCoreの初期化を二重に呼ばないこと
`HeadOutputDevice.Screen` を設定するとエンジンが自動でCSCoreを初期化します。手動で `CSCoreAudioInputDriver.Initialize()` を呼ぶと `Audio refresh function already registered` エラーでクラッシュします。

### ログの確認
全ログはコンソール出力と `Logs/` ディレクトリ内のファイルの両方に記録されます。オーディオ関連の問題をデバッグするには、ログ内の以下のキーワードを確認：
- `Initialized Audio Input MMDevice` — オーディオデバイスの検出状況
- `DefaultCapture` / `DefaultOutput` — 既定のマイク/スピーカー
- `Starting audio device` — オーディオ出力の開始

### エンジンのUpdate Loopについて
`Engine.RunUpdateLoop()` は専用スレッドで10msインターバルで呼び出しています。CLIコマンドはメインスレッドで受け付け、`GlobalCoroutineManager.Post()` でエンジンスレッドに安全に送信しています。

## ヘッドレス/UI未依存のコマンド実装について

ヘッドレスクライアントでは、FrooxEngineのUI（`Canvas`や`Slot`上に構築されるインスペクターやメニュー）に依存するコマンドは使用できません。APIは内部コンポーネントを直接操作する必要があります。

### データモデルの変更はRunSynchronouslyでラップする
`FrooxEngine` 内のノード（`world.LocalUser` やコンポーネント）のプロパティを変更する際は、必ず **`world.RunSynchronously(() => { ... })`** 内で行ってください。さもないと `Modifications from a non-locking thread are disallowed!` というエラーが発生します（例外: `engine.AudioSystem` のようなグローバルマネージャーはスレッドセーフな場合があります）。

### クラウド・セッション管理 (`engine.Cloud`)
*   **ログイン**: `engine.Cloud.Session.Login(username, new PasswordLogin(password), secretMachineId, rememberMe: true, totp: null)` を使用します。非同期で通信します。
*   **ログアウト**: `engine.Cloud.Session.Logout(isManual: true)`
*   **セッション一覧**: `engine.Cloud.Sessions.GetSessions(List<SessionInfo>)`

### ワールド・フォーカス管理 (`engine.WorldManager`)
*   **現在参加中のワールド一覧**: `engine.WorldManager.Worlds`（ユーザーが参加しているすべてのワールドや`Userspace`）
*   **ワールド切り替え（フォーカス）**: `engine.WorldManager.FocusWorld(World)`

### アバター・ロコモーション操作 (`FrooxEngine.LocomotionController` など)
*   ロコモーションモジュール等のコンポーネントは、`world.LocalUser.Root.Slot` の下層（`Locomotion Modules`など）に構築されています。
*   `GetComponentInChildren<T>()` などを使いコンポーネントを探し出します。
*   モジュール名（表示名）は `ILocomotionModule.LocomotionName` として存在しますが `LocaleString` 構造体（`struct`）であることに注意してください。値型のため `?.` ではなく `.ToString()` のような直接アクセスが必要です。