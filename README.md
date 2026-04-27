# AudioClient

AudioClient は、Resonite の音声専用クライアントです。

通常のゲーム画面を表示しないため、別のゲームを遊びながら Resonite の友達と会話したいときや、強力なグラフィックボードがない PC で使いたいときに便利です。

## 概要

`AudioClient.GUI` は、Resonite の 3D 画面を描画せずにセッションへ参加し、音声での会話を中心に利用するためのクライアントです。

Resonite に軽く接続しておきたいとき、作業や別ゲームの裏で通話したいとき、できるだけ GPU 負荷を抑えて使いたいときに向いています。

## インストール

1. 最新リリースをダウンロードします。
2. ZIP ファイルを任意のフォルダに展開します。
3. Windows では `AudioClient.GUI.exe` を起動します。

利用前に、Steam から Resonite をインストールしておく必要があります。

初回起動時、GUI は Steam の既定インストール先から Resonite を自動で探します。見つからない場合は、Resonite のインストールフォルダを選択するよう求められ、その設定は次回以降も保存されます。

## Linux 実行について

`AudioClient.GUI` は Avalonia を使っているため、ネイティブ Linux の .NET 実行を前提にビルドできます。

```bash
dotnet build AudioClient.sln
dotnet run --project AudioClient.GUI/AudioClient.GUI.csproj
```

Proton/Wine 経由で Windows ビルドを起動すると、Windows 向け .NET ランタイムのインストールを要求される場合があります。Linux では Proton ではなく、Linux の `dotnet` で直接起動してください。

現時点の Linux 対応は実装途中のため、環境によっては一部のエンジン機能（特に音声デバイス周り）が動作しない場合があります。

## CLI 版について

CLI 版のコマンド一覧と使い方は [`CLI.md`](CLI.md) に移動しました。

## License and redistribution

The source code in this repository is licensed under the MIT License.

This applies only to original AudioClient code in this repository. Resonite,
FrooxEngine, SkyFrost, Awwdio, Elements, and other game-provided assemblies,
assets, names, and materials remain under their own licenses and terms.

When publishing this project:

- Do not include Resonite-provided DLLs or assets in this repository.
- Do not imply that Resonite/FrooxEngine content is relicensed under MIT.
- Prefer release artifacts that contain only AudioClient-built files and
  instruct users to point the app at their own Resonite installation.

See `THIRD-PARTY-NOTICES.md` for a summary of third-party dependencies used by
this repository.

## GUI settings

`AudioClient.GUI` now includes a settings dialog from the status bar gear icon.
You can toggle automatic switching to the AudioClient Avatar when joining a
session, and the setting is saved in the GUI settings file.
