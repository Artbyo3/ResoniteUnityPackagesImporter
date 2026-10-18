# インストール

このガイドでは、Unity Package Importer の動作要件、新規インストール、設定、アップデート、およびアンインストール手順について説明します。

## 前提要件

- **Resonite**: Windows 上で動作する正規の Resonite。
- **ResoniteModLoader (RML)**: お使いの Resonite 環境にインストールおよびセットアップ済みであること。

## 新規インストール

1. リリースアーカイブ（`UnityPackageImporter-x.x.x.zip`）をダウンロードします。
2. リリースノートに記載されたチェックサムとダウンロードしたファイルを照合します。
3. アーカイブを展開します：
    - `UnityPackageImporter.dll` を Resonite の `rml_mods` フォルダに配置します。
    - `YamlDotNet.dll` を Resonite の `rml_libs` フォルダに配置します（まだ存在しない場合）。
4. ResoniteModLoader を有効にして Resonite を起動します。
5. Mod ログを確認し、`UnityPackageImporter` が起動時エラーなしで読み込まれていることを確認します。

## アップデート

1. Resonite を完全に終了します。
2. `rml_mods` フォルダ内の `UnityPackageImporter.dll` を新しいバージョンに置き換えます。
3. 新しいリリースで依存ライブラリが追加・更新された場合は、`rml_libs` フォルダ内の対応するファイルも更新します。
4. Resonite を再起動します。

## ロールバックとアンインストール

- **ロールバック**: 以前のバージョンに戻すには、`rml_mods` の `UnityPackageImporter.dll` を以前の DLL に置き換えます。
- **アンインストール**: `rml_mods` から `UnityPackageImporter.dll` を削除します。他の Mod で `YamlDotNet.dll` を使用していない場合は、`rml_libs` から削除しても構いません。

## 設定オプション

本インポーターは ResoniteModLoader を通じた設定に対応しています。Mod 設定ファイル（Config）またはゲーム内の Mod 設定画面からカスタマイズが可能です：

| 設定項目 | デフォルト値 | 説明 |
| --- | --- | --- |
| `importPrefab` | `true` | ドロップされた `.unitypackage` ファイルを検出し、インポートステーション上で対応 Prefab やシーンをステージング・再構築します。 |
| `dumpPackageContents` | `false` | `importPrefab` と同時に有効にすると、Prefab だけでなくパッケージ内の全ファイルをワールド内に抽出・インポートします。 |
| `importAsRawFiles` | `false` | 有効にすると、インポーターのステーションを経由せず、Resonite のネイティブファイルインポーターを使用して直接通常インポートします。 |
