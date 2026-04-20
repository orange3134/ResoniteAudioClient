# AudioClient CLI — 実装ノウハウ

CLIクライアントの固有知識です。FrooxEngine全般の知識はルートの `CLAUDE.md` を参照してください。

## ブートストラップパターン

```
Main(args)
  ├─ PATH に runtimes/win-x64/native/ を追加
  ├─ AssemblyResolve ハンドラを登録
  ├─ アプリディレクトリの全DLLをプリロード
  └─ [NoInlining] RunEngine(args, appDir) を呼ぶ

[NoInlining] RunEngine(args, appDir)
  ├─ EngineHost.StartAsync(...) でエンジン起動
  ├─ ConsoleCommandLoop でコマンド受け付け
  └─ 終了時 engineHost.Shutdown()
```

`Main` 自体にFrooxEngine型を含めないことが重要です（JITのアセンブリ解決タイミング問題）。`[MethodImpl(NoInlining)]` がその境界です。

## コマンドループ

コマンドはメインスレッド（`Console.ReadLine()`）で受け付け、`host.PostToEngine(action)` でエンジンスレッドに送信します。`PostToEngine` の内部は `GlobalCoroutineManager.Post()` です。

非同期コマンド（ログイン等）は `await` を使い、結果をコンソールに出力します。

## デバッグのヒント

### CSCoreの初期化を二重に呼ばないこと
`HeadOutputDevice.Screen` を設定するとエンジンが自動でCSCoreを初期化します。手動で `CSCoreAudioInputDriver.Initialize()` を呼ぶと `Audio refresh function already registered` エラーでクラッシュします。

### ログの確認
全ログはコンソール出力と `Logs/` ディレクトリ内のファイルの両方に記録されます。オーディオ関連のデバッグには以下のキーワードを確認：
- `Initialized Audio Input MMDevice` — オーディオデバイスの検出状況
- `DefaultCapture` / `DefaultOutput` — 既定のマイク/スピーカー
- `Starting audio device` — オーディオ出力の開始

### エンジンのUpdate Loopについて
`Engine.RunUpdateLoop()` は専用スレッドで10msインターバルで呼び出しています。CLIコマンドはメインスレッドで受け付け、`GlobalCoroutineManager.Post()` でエンジンスレッドに安全に送信しています。
