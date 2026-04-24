---
name: IStaticAssetProvider.URL でアセットURLをダウンロード前に取得
description: provider.Asset?.AssetURL はダウンロード完了後でないと null。IStaticAssetProvider.URL で即座に取得できる
type: project
---

## 事実

`IAssetProvider<T>.Asset` はアセットの非同期ダウンロードが完了するまで null。`provider.Asset?.AssetURL` はこのためポスト受信直後に null を返す。

`StaticAssetProvider` は `Sync<Uri> URL` フィールドを持ち、これはコンポーネントが同期された時点で設定済み。`IStaticAssetProvider` インターフェースの `URL` プロパティ経由で読める。

**How to apply:** アセットURLを取得する際は `IStaticAssetProvider` にキャストして `URL` を読む。

```csharp
private static Uri? GetProviderAssetUrl(IAssetProvider<Texture2D>? provider)
{
    if (provider == null) return null;
    if (provider is IStaticAssetProvider staticProvider)
        return staticProvider.URL; // ダウンロード待ち不要
    return provider.Asset?.AssetURL; // フォールバック
}
```

## resdb:// → https:// 変換

```
resdb:///hash.webp → https://assets.resonite.com/hash
```

`ToHttpAssetUrl` で変換。拡張子は除去する（CDN はハッシュのみで提供）。
Resonite の CDN は認証不要でコンテンツアドレッシング（ハッシュが検証を兼ねる）。
