---
name: FrooxEngine DynamicReferenceVariable 同期タイミング問題
description: リモートクライアントからスロットが届いた際、DynamicReferenceVariable の参照先が即座に解決されない問題とリトライ設計
type: project
---

## 事実

`ChildAdded` イベントが発火した時点では、リモートクライアントから届いたスロットの構造が完全に同期されていないことがある。特に `DynamicReferenceVariable<T>` コンポーネント自体、またはその `Reference.Target` が null のまま届く。

**Why:** FrooxEngine のネットワーク同期はコンポーネントを順番にストリーミングするため、スロット到着（`ChildAdded`）とその中のコンポーネント完全同期は別タイミング。

## `GlobalCoroutineManager.Post` のリトライは速すぎる

`GlobalCoroutineManager.Post` はエンジンスレッドのキューを即座に消費するため、120回のリトライが FrooxEngine の同期フレームより遥かに速く完了してしまう。リトライを連打しても参照が解決される前に全試行を使い切る。

**How to apply:** リトライには `Task.Delay(100)` で間隔を入れること。

```csharp
// NG: 連打になり sync が追いつかない
if (attempt < Max) SchedulePostAdded(child, attempt + 1);

// OK: 100ms 間隔でリトライ
_ = Task.Delay(100).ContinueWith(_ =>
{
    try { engine.GlobalCoroutineManager.Post(__ => ProcessPostAttempt(child, attempt + 1), null!); }
    catch { }
});
```

## IsPostReady は全コンテンツタイプを網羅すること

`IsPostReady` が特定のコンテンツタイプ（Image など）のチェックを欠くと、URL が null のまま「準備完了」と誤判定し、以降のリトライが一切行われなくなる。

```csharp
// Image コンテンツで imageUrl が null なら未完了
if (content.Type == "Image" && string.IsNullOrEmpty(content.ImageUrl))
    return false;
```

## 実装パターン (AudioClient.Core/Services/ChatService.cs)

- `PostReadRetryDelayMs = 100` (100ms 間隔)
- `PostReadMaxAttempts = 120` (最大 12 秒)
- `SchedulePostAdded` → 即 `GlobalCoroutineManager.Post` → `ProcessPostAttempt`
- `ProcessPostAttempt` が失敗時に `Task.Delay` → `GlobalCoroutineManager.Post` → 次の `ProcessPostAttempt`
