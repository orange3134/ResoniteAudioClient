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

## Avalonia コンパイル済みバインディングの注意点（ネストパス）

`x:DataType` を指定したコンパイル済みバインディングで `{Binding Login.IsVisible}` のようなネストパスを使う場合、中間プロパティ（`Login`）が `[ObservableProperty]` でないと Avalonia が末端の `PropertyChanged` を購読しないことがあります。結果として VM 側でプロパティを変更しても UI が更新されません。

**対処パターン**: 外部から `IsVisible` を制御したいコントロールは、コードビハインドで `DataContextChanged` を購読し、VM の `PropertyChanged` を直接監視してコントロールの `IsVisible` を更新します。XAML 側には `IsVisible="False"` の静的初期値だけ置き、バインディングは書きません。

```csharp
DataContextChanged += (_, _) =>
{
    if (_vm != null) _vm.PropertyChanged -= _handler;
    _vm = DataContext as MyViewModel;
    if (_vm != null)
    {
        _handler = (_, e) => { if (e.PropertyName == nameof(MyViewModel.IsVisible)) IsVisible = _vm.IsVisible; };
        _vm.PropertyChanged += _handler;
        IsVisible = _vm.IsVisible;
    }
};
```

## ログインパネルの表示制御

エンジン初期化完了時に `Login.IsVisible = !host.Auth.IsLoggedIn` と `Login.IsLoggedIn = host.Auth.IsLoggedIn` を設定します（保存済みセッションで自動ログインされた場合はパネルを表示しない）。

`LoginStateChanged(true)` でパネルを閉じます。`LoginStateChanged(false)` ではパネルを自動表示しない（`Login.IsLoggedIn` だけ更新）。これにより、ネットワーク一時切断などで誤ってパネルが開くことを防いでいます。

ログアウト後の再ログインは StatusBar の "Sign In" ボタンから `Login.ShowLogin(isLoggedIn, username)` を呼びます。ログイン済み状態でパネルを開くと、ユーザー名とサインアウトボタンが表示されます。サインアウト後はパネルがログインフォームに切り替わります。

## BrowseSessionsViewModel のリフレッシュ

`OnRefreshRequested` コールバックは `GetActiveSessions()` を呼んでUIスレッドで `Update()` します。エンジン負荷を考慮し、ユーザー操作（Refresh ボタン）でのみ呼ぶ設計です（自動ポーリングは行わない）。

`GetActiveSessions()` はクラウドAPIへのブロッキング呼び出しのため、UIスレッドから呼ぶと固まります。`OnRefreshRequested` は必ずバックグラウンドスレッドから呼ぶこと。`InitializeEngineAsync` 内（`Task.Run` の中）でコールバックを設定した直後に呼び出せば、バックグラウンドスレッドで実行されます。

Browse タブはデフォルト選択状態で起動するため、エンジン準備完了時に自動リフレッシュが必要です。`InitializeEngineAsync` の末尾（`OnRefreshRequested` を設定した後）で `BrowseSessions.OnRefreshRequested?.Invoke()` を呼んでいます。タブ切り替え時は `SelectLeftTabCommand` で Browse が選ばれた際にも呼びます。

## IsVisible と DataContext を同一要素に設定してはいけない

```xml
<!-- NG: IsVisible が BrowseSessionsViewModel に対して解決されてしまう -->
<v:BrowseSessionsPanel IsVisible="{Binding IsBrowseTabSelected}"
                       DataContext="{Binding BrowseSessions}" />
```

Avalonia では、`DataContext="{Binding X}"` を設定した要素上の他のバインディングは、変更後の DataContext（`X` の型）に対して解決されます。`IsVisible` のような表示制御バインディングは親の DataContext（`MainViewModel`）で解決したいため、ラッパー要素で分離します。

```xml
<!-- OK: Border の DataContext は MainViewModel のまま -->
<Border IsVisible="{Binding IsBrowseTabSelected}">
    <v:BrowseSessionsPanel DataContext="{Binding BrowseSessions}" />
</Border>
```
