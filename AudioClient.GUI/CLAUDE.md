# AudioClient.GUI — 実装ノウハウ

AvaloniaベースのGUIクライアントの固有知識です。FrooxEngine全般の知識はルートの `CLAUDE.md` を参照してください。

## ブートストラップパターン

```
[STAThread] Main(args)
  ├─ PATH に runtimes/win-x64/native/ を追加（SkiaSharpネイティブDLL含む）
  ├─ AssemblyResolve ハンドラを登録
  ├─ アプリディレクトリの全DLLをプリロード
  └─ [NoInlining] StartGui(args, appDir) を呼ぶ

[NoInlining] StartGui(args, appDir)
  └─ AppBuilder.Configure<App>().UsePlatformDetect()
       .StartWithClassicDesktopLifetime(args)
```

`StartWithClassicDesktopLifetime` はUIスレッドをブロックするため、エンジン初期化はその内部（`App.OnFrameworkInitializationCompleted` → `MainViewModel` コンストラクタ内の `Task.Run`）で非同期に行います。

## エンジン初期化とUIの統合

`MainViewModel` のコンストラクタで `Task.Run(() => InitializeEngineAsync(appDir, args))` を呼びます。エンジン初期化中はUIに「Initializing...」を表示し、完了後に各ViewModelをバインドします。

イベントハンドラはすべて `_ = Dispatcher.UIThread.InvokeAsync(() => ...)` でUIスレッドに転送します。

エンジンミューテーションは `host.PostToEngine(action)` → `GlobalCoroutineManager.Post()` でエンジンスレッドに送ります。

## 状態変化の検出

FrooxEngineの多くの状態はイベントを持たないため、`EngineHost` 内の500msポーリングタイマーがスナップショット比較で変化を検出しています：
- セッション一覧 / メンバー一覧 / ミュート状態 / 音量

## MVVMパターン (CommunityToolkit.Mvvm)

- `[ObservableProperty]` — プロパティ自動生成（`_camelCase` フィールドから `PascalCase` プロパティ）
- `[RelayCommand]` — コマンド自動生成（`void/Task` メソッドから `ICommand`）
- `partial void OnXxxChanged(T value)` — プロパティ変更フック

ViewModelから外部アクションへの橋渡しは `Action` / `Func` プロパティ（`OnJoinRequested`, `OnLeaveRequested` 等）で行い、`MainViewModel` でエンジン操作と接続します。

## Avalonia XAML の注意点

### コンパイル済みバインディング
`x:DataType="vm:XxxViewModel"` を必ず指定してください。指定しないとバインディングエラーが実行時まで検出されません。

### ネストしたバインディング
ItemsControl 内から親VMのコマンドにバインドする場合：
```xml
Command="{Binding $parent[ItemsControl].((vm:ParentViewModel)DataContext).SomeCommand}"
```

### Popup の Placement
`PlacementMode="Top"` は廃止済み。`Placement="Top"` を使うこと。

### コンバーター
`{x:Static v:ConverterClass.Instance}` でシングルトンを参照します（`xmlns:v` の追加を忘れずに）。

`BoolToBrushConverter` は `ConverterParameter="TrueColorKey:FalseColorKey"` の形式でDiscordカラーパレットのキーを渡します。

### StringFormat
```xml
Text="{Binding Value, StringFormat={}{0:P0}}"
```
`{}` のエスケープが必要です。

## カスタムタイトルバー

```xml
<Window ExtendClientAreaToDecorationsHint="True"
        ExtendClientAreaChromeHints="NoChrome"
        SystemDecorations="Full">
```

タイトルバーのドラッグ：
```xml
<Border PointerPressed="TitleBar_PointerPressed">
```
```csharp
private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        BeginMoveDrag(e);
}
```

タイトルバー内のTextBlockなど非インタラクティブ要素には `IsHitTestVisible="False"` を付けてドラッグを通過させること。

## SkiaSharp ネイティブDLLのコピー

Avaloniaは `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll` 等のネイティブDLLを `runtimes/win-x64/native/` に格納します。ゲームフォルダにコピーしないと `DllNotFoundException: libSkiaSharp` でクラッシュします。

csprojのpost-buildで明示的にコピーする必要があります：
```xml
<ItemGroup>
  <NativeFiles Include="$(TargetDir)runtimes\win-x64\native\*" />
</ItemGroup>
<Copy SourceFiles="@(NativeFiles)"
      DestinationFolder="$(AppTargetDir)runtimes\win-x64\native\" />
```

ゲームフォルダの `runtimes/win-x64/native/` はすでにPATHに含まれているため、このパスに置けばOSが自動で解決します。

## ログインパネルの表示制御

エンジン初期化完了時に `Login.IsVisible = !host.Auth.IsLoggedIn` と `Login.IsLoggedIn = host.Auth.IsLoggedIn` を設定します（保存済みセッションで自動ログインされた場合はパネルを表示しない）。

`LoginStateChanged(true)` でパネルを閉じます。`LoginStateChanged(false)` ではパネルを自動表示しない（`Login.IsLoggedIn` だけ更新）。これにより、ネットワーク一時切断などで誤ってパネルが開くことを防いでいます。

ログアウト後の再ログインは StatusBar の "Sign In" ボタンから `Login.ShowLogin(isLoggedIn, username)` を呼びます。ログイン済み状態でパネルを開くと、ユーザー名とサインアウトボタンが表示されます。サインアウト後はパネルがログインフォームに切り替わります。

## BrowseSessionsViewModel のリフレッシュ

`OnRefreshRequested` コールバックは `GetActiveSessions()` を呼んでUIスレッドで `Update()` します。エンジン負荷を考慮し、ユーザー操作（Refresh ボタン）でのみ呼ぶ設計です（自動ポーリングは行わない）。
