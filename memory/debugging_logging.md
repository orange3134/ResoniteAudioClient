---
name: AudioClient デバッグログ出力方法
description: エンジン側は UniLog、GUI側は System.Diagnostics.Debug が FrooxEngine ログに出ないためファイルログを使う
type: project
---

## エンジン側（AudioClient.Core）

`Elements.Core.UniLog.Log(string)` で FrooxEngine のログファイルに `[INFO]` として出力される。

```csharp
Elements.Core.UniLog.Log($"[ChatService] value={x}");
```

ログファイルは `AudioClient.GUI\bin\Debug\Logs\` に生成される。

## GUI側（AudioClient.GUI）

`System.Diagnostics.Debug.WriteLine` は FrooxEngine のログファイルには**出力されない**。デバッガなしでは事実上見えない。

代わりにファイルログを使う：

```csharp
System.IO.File.AppendAllText(
    System.IO.Path.Combine(AppDir, "img_debug.log"),
    $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
```

出力先はビルド成果物フォルダ（ゲームフォルダ）内。

## 問題の切り分けパターン

画像が表示されない場合の切り分け：
1. エンジンログで `TryReadImageUrl` が URL を返しているか確認
2. `all paths failed` → エンジン側の参照解決問題（sync タイミング）
3. URL あり → GUI 側の HTTP フェッチ or Bitmap 生成問題（ファイルログで確認）
4. `FetchAsync` が呼ばれない（ファイルログなし）→ `ImageUrl` が null で渡っている
