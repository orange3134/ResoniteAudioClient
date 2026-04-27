# 概要
ResoniteのFrooxEngineをUnityの描画エンジンなしで起動し、セッションに参加して空間オーディオをリアルタイム再生するクライアントです。
CLIとGUIの2つの実行ファイルがあり、共通エンジンロジックを `AudioClient.Core` ライブラリで共有しています。

```
AudioClient.sln
├── AudioClient.Core/   ← エンジン操作ロジック（classlib）
├── AudioClient/        ← CLIクライアント
└── AudioClient.GUI/    ← AvaloniaベースのGUIクライアント
```

# 開発
FrooxEngineをデコンパイルしたコードが `./reso-decompile` にあります。必要に応じて参照してください。

# ドキュメントの整理
新しいコマンドを実装したり、既存のコマンドを修正したりした場合は、コマンドの使い方をREADME.mdにまとめてください。

# AGENTS.mdの更新
開発中にノウハウや留意点が見つかった場合は、該当するAGENTS.mdに追記してください（全体共通なら本ファイル、CLI固有なら `AudioClient/AGENTS.md`、GUI固有なら `AudioClient.GUI/AGENTS.md`）。

# 公開・ライセンス
- リポジトリ自体をMITで公開するのは問題ないが、MITが適用されるのは `AudioClient` のオリジナルコード部分だけと明記すること
- `Resonite` / `FrooxEngine` / ゲーム由来のDLL・アセット・名称MITで再ライセンスしないこと
- 配布物は可能な限り自前ビルド成果物のみにし、Resonite本体はユーザーの正規インストールを利用させること

---

# FrooxEngine 共通知識

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

**対策：** FrooxEngineを使う全コードを `[MethodImpl(MethodImplOptions.NoInlining)]` を付けた別メソッドに分離し、`Main` → `[NoInlining]メソッド` の呼び出し構造にします。CLIは `RunEngine()`、GUIは `StartGui()` がその境界です。

### 全DLLのプリロードが必須
起動時に `Assembly.LoadFrom()` でアプリケーションディレクトリ内の全 `.dll` をプリロードしています。これがないと、エンジンの型スキャナが `Awwdio`, `PhotonDust` 等の Data Model Assembly を登録できず、セッション参加時に `CompatibilityError` が発生します。

プリロードで `BadImageFormatException`（ネイティブDLL）や一部の読み込みエラーが出ますが、これらは無害なので `catch` でスキップしています。

### ネイティブDLLのPATH追加
SteamAudioの `phonon.dll` 等のネイティブDLLは `runtimes/win-x64/native/` に格納されています。.NET のデフォルトでは探索パスに含まれないため、起動直後に `Environment.SetEnvironmentVariable("PATH", ...)` で追加する必要があります。

Linux対応を行う場合は `win-x64` 固定で探索せず、OSに応じたRID（例: `linux-x64`, `linux-musl-x64`）も順に探索すること。

## csproj の設定

### FrooxEngine 参照の書き方
```xml
<Reference Include="FrooxEngine">
  <HintPath>$(GamePath)FrooxEngine.dll</HintPath>
  <Private>false</Private>
  <SpecificVersion>false</SpecificVersion>
</Reference>
```
- `Private=false` — ビルド出力にResoniteのDLLをコピーしない（ゲームフォルダに既にあるものを使う）
- `SpecificVersion=false` — ビルド時にバージョン完全一致を要求しない（ランタイムの照合は `AssemblyResolve` で対応）

### PostBuild のコピー先
各プロジェクトのビルド成果物は `$(GamePath)AudioClient\` のような専用サブフォルダに自動コピーします。Resonite本体DLLは引き続きゲームフォルダ直下のものを使うため、サブフォルダ配置にした場合は `AssemblyResolve` と全DLLプリロードの探索先に **アプリフォルダと親のゲームフォルダの両方** を含める必要があります。加えて `engine.Initialize(...)` に渡すベースディレクトリはアプリフォルダではなく **親のResoniteゲームフォルダ** を使ってください。`RuntimeData\Local.bin` や `Locale\` はそこ基準で読まれるためです。`.pdb` は除外（`AudioClient.exe` の `.pdb` のみ含める）。

## セッション互換性チェックの仕組み
エンジンはセッション参加時に以下を照合します：
1. `SystemCompatibilityHash` — 全アセンブリの型定義から算出した全体ハッシュ
2. 各 Data Model Assembly の `CompatibilityHash` — アセンブリ内の型のフィールド・メソッド構造のMD5ハッシュ

**同じバージョンのResonite**を使っている限り、これらは一致します。異なるバージョン間では参加できません（これはResoniteの仕様です）。

## FrooxEngine API リファレンス

### スレッド安全性
`FrooxEngine` 内のノード（`world.LocalUser` やコンポーネント）のプロパティを変更する際は、必ず **`world.RunSynchronously(() => { ... })`** 内で行ってください。さもないと `Modifications from a non-locking thread are disallowed!` というエラーが発生します（例外: `engine.AudioSystem` のようなグローバルマネージャーはスレッドセーフな場合があります）。

### DynamicVariable の同期タイミング
リモートユーザーが作成・複製した Slot は、`ChildAdded` が発火した時点で子 Slot、コンポーネント、`DynamicVariableSpace` への登録、`DynamicValueVariable<T>.Value` の同期がすべて完了しているとは限りません。`DynamicVariableSpace.TryReadValue<T>()` は readable な変数が space に登録済みでないと `false` / default を返すため、受信直後に空値を UI に確定しないでください。実値が読めるまで数フレーム以上リトライするか、必要に応じて `DynamicValueVariable<T>` コンポーネントの `Value` を直接読むフォールバックを用意します。

`Texture2D` は `IWorldElement` ではなく asset なので `DynamicReferenceVariable<Texture2D>` としては扱えません。AudioClientWorld のチャット画像 (`Content/Type == "Image"`) は `Content/Content` に `IAssetProvider<Texture2D>` として入っているため、provider 参照を読み、`provider.Asset.AssetURL` から実体 URL を辿ります。

DynamicVariable の読み取りは型完全一致です。`Content/Content` の画像 provider を読むときは `IAssetProvider<Texture2D>` で読む必要があります。

チャットなどでユーザーアイコンを表示する場合、投稿 Slot 内にアイコン URL が入っているとは限りません。投稿者名から `world.AllUsers` の `UserID` を解決し、MemberList と同じ Cloud profile 取得 (`Contacts.GetUserIconUrlAsync`) で補完すると表示できるケースが多いです。

### クラウド・セッション管理 (`engine.Cloud`)
- **ログイン**: `engine.Cloud.Session.Login(username, new PasswordLogin(password), secretMachineId, rememberMe: true, totp: null)`
- **ログアウト**: `engine.Cloud.Session.Logout(isManual: true)`
- **セッション一覧**: `engine.Cloud.Sessions.GetSessions(List<SessionInfo>)`

### TOTP 必須時の判定
`engine.Cloud.Session.Login(...)` は TOTP が必要なとき例外ではなく `CloudResult` を返し、`result.Content == "TOTP"` になります。GUI や CLI で二段階ログインを実装するときは、この値を見て追加の TOTP 入力を促してください。

### ワールド・フォーカス管理 (`engine.WorldManager`)
- **現在参加中のワールド一覧**: `engine.WorldManager.Worlds`
- **ワールド切り替え（フォーカス）**: `engine.WorldManager.FocusWorld(World)`

### セッションへの参加 (`Userspace.JoinSession`)
- `Userspace.JoinSession(IEnumerable<Uri>)` の複数URL版を優先して使う（LNL → Steam の優先度順で試みるため接続成功率が上がる）
- `res-steam://` URLはSteam P2Pを使うためヘッドレス環境では動作しません。`SessionInfo.GetSessionURLs()` で全URLを取得し `lnl-nat://` を優先してください
- `SessionInfo.GetSessionURLs()` は `List<Uri>` を返し、無効なURLを自動で除外します

### セッションの開始
- レコードURLから: `Userspace.OpenWorld(new WorldStartSettings(uri))`（非同期）
- 組み込みテンプレートから: `Userspace.StartSession(preset.Method)`（同期）。プリセットは `WorldPresets.Presets` で列挙
- `Userspace.StartSession` の引数に `FrooxEngine.Store.Record` 型が含まれるため、csprojに `FrooxEngine.Store.dll` の参照追加が必要

### セッション設定の変更
- `world.Name = "..."` と `world.AccessLevel = SessionAccessLevel.Contacts` のセッターは内部で `RunSynchronously` を呼ぶためスレッドセーフ
- `world.AllowUserToJoin(userId)` はデータモデルの変更なので `world.RunSynchronously()` でラップが必要

### コンタクト・招待管理 (`engine.Cloud.Contacts` / `engine.Cloud.Messages`)
- **コンタクト一覧**: `engine.Cloud.Contacts.ForeachContactData(Action<ContactData>)`
- **オンライン状態**: `ContactData.CurrentStatus.OnlineStatus`（`SkyFrost.Base.OnlineStatus`: Offline/Invisible/Away/Busy/Online/Sociable）
- **コンタクトの現在セッション**: `ContactData.CurrentSessionInfo`（プライベートや不可視は `null`）
- **`UserSessionMetadata`にセッション名は含まれません** — プライバシー設計によりアクセスレベル・IsHost・SessionHiddenのみ保持
- **招待送信**: `engine.Cloud.Messages.GetUserMessages(userId).SendInviteMessage(sessionInfo)`。事前に `world.GenerateSessionInfo()` が必要

### アバター・ロコモーション操作
- ロコモーションモジュールは `world.LocalUser.Root.Slot` の下層（`Locomotion Modules`）に構築
- `GetComponentInChildren<T>()` でコンポーネントを検索
- `ILocomotionModule.LocomotionName` は `LocaleString` 構造体（値型）なので `?.` は使えない

### アバター着替え
- 公開レコードからアバターへ着替えるときは `Engine.RecordManager.FetchRecord(uri)` で `Record` を取得し、`world.TryEquipAvatar(record)` を使う。FrooxEngine 側の権限チェック、ロード、`InventoryItem` の unpack、`AvatarManager` への装着までまとめて処理してくれる
- `world.TryEquipAvatar(record)` は `world.State == Running` かつ `world.LocalUser.Root` が構築済みになってから呼ぶ
- セッション参加直後は Resonite 標準の「デフォルトアバター自動装備」が後から走ることがあり、そこで上書きされる。参加検知直後に即 `TryEquipAvatar` するのではなく、`world.RunInSeconds(...)` で数秒遅らせてから装備し、必要ならさらに数秒後に 1 回だけ再試行すると安定する

### ユーザー位置情報 (`UserRoot`)
- `user.Root.HeadPosition` → `float3`（ワールド空間XYZ）
- `user.Root.HeadFacingRotation` → `floatQ`（クォータニオン）。`.EulerAngles.y` でY軸回転（度）
- `floatQ.EulerAngles` は `float3`、単位は度（ラジアンではない）

### 起動オプション (`LaunchOptions`)
- `LaunchOptions.GetLaunchOptions(args)` は `-DataPath`/`-Data`、`-CachePath`/`-Cache`、`-LogsPath` などを自動パース
- デフォルト値は `string.IsNullOrEmpty(options.DataDirectory)` で未指定確認してから代入（コマンドライン引数を上書きしない）

## GUI 配布時の注意
- GUI は任意ディレクトリ配置を前提にし、FrooxEngine の探索先は「保存済み設定 → Steam の既定インストール先 → アプリ隣接の Resonite フォルダ候補」の順で解決する
- 保存済みパスが無効だったり Steam 既定パスに Resonite が無い場合は、GUI でユーザーにインストール先フォルダを選ばせて保存する
- `-DataPath` 未指定時の既定値は通常の LocalLow ではなく `アプリ配置先/DataPath` にする。これで通常の Resonite と同時起動してもデータ競合しにくい
