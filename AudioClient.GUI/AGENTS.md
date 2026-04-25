# GUI 固有メモ

- Avalonia 11.2.7 の `TextBox` は Windows の日本語 IME 変換中に `Text` バインディングを逐次 ViewModel へ反映すると、確定時に文字が重複することがある。GUI の入力欄は `UpdateSourceTrigger=LostFocus` を基本にし、Enter で即時処理したい箇所だけ `ImeAwareTextBox.FlushTextBindingToSource()` で明示同期する。
- IME 変換確定に Enter を使う欄では、送信や検索実行の前に `ImeAwareTextBox.HasActiveImeComposition` を見て、変換中の Enter をアプリ側ショートカットとして扱わないこと。
