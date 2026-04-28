# GUI 固有メモ

- Avalonia の `TextBox` は Windows の日本語 IME 変換中に `Text` バインディングを逐次 ViewModel へ反映すると、確定時に文字が重複することがある。UI の入力欄は `UpdateSourceTrigger=LostFocus` を基本にし、Enter で即時処理したい箇所は `ImeAwareTextBox.FlushTextBindingToSource()` で明示同期する。
- IME 変換確定に Enter を使う場面では、送信や検索実行の前に `ImeAwareTextBox.HasActiveImeComposition` を見て、変換中の Enter をアプリ側ショートカットとして扱わない。
- `GuiSettingsStore.Save` はファイル全体を書き換えるので、UI 設定を追加するときは既存設定を `Load()` して未変更の項目を保持したまま保存する。
- ChatPanel の画像貼り付けは TextBox の `PastingFromClipboard` と ChatPanel 全体の tunneling `KeyDown` の両方で拾う。Windows のスクリーンショット等は Avalonia の `TryGetBitmapAsync()` で取れないことがあるため、CF_DIB/CF_DIBV5 を Win32 Clipboard API で読み、PNG 一時ファイルに変換して既存の添付処理に渡す。
