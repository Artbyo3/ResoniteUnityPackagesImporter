# Standard および PBS マテリアル

**ステータス: 対応 (Supported)**。Unity の標準物理ベースシェーディング（PBS Metallic ワークフロー）で作成されたマテリアルは、Resonite ネイティブの `PBS_Metallic` マテリアルに直接変換されます。

## 対応プロパティマッピング

| Unity プロパティ | Resonite プロパティ | 説明 |
| --- | --- | --- |
| `_MainTex`, `_BaseMap`, `_MainTexture`, `_1st_ShadeMap` | `AlbedoTexture` | メインのディフューズ / ベースカラーマップ |
| テクスチャの Scale & Offset | `TextureScale`, `TextureOffset` | UV0 上のテクスチャ座標変換（タイリング・オフセット） |
| `_Color` | `AlbedoColor` | ベースカラーのカラー乗算係数 |
| `_BumpMap` | `NormalMap` | ノーマルマップ / バンプマップ（ノーマルマップフラグ付きでインポート） |
| `_BumpScale` | `NormalScale` | ノーマルマップの適用強度 |
| `_MetallicGlossMap` | `MetallicMap` | メタリック / スムースネスのパックテクスチャマップ |
| `_Metallic` | `Metallic` | メタリック値 |
| `_Smoothness` | `Smoothness` | 表面のスムースネス値 |
| `_EmissionMap` | `EmissiveMap` | 静的な発光（エミッション）テクスチャマップ |
| `_EmissionColor` | `EmissiveColor` | 発光カラーの乗算係数 |
| `_OcclusionMap` | `OcclusionMap` | アンビエントオクルージョン（AO）マップ |

## 動作および注意事項

- **マテリアル割り当て**: インポートされたメッシュレンダラーの対応するサブメッシュスロットに正確に割り当てられます。
- **コンパニオンパッケージによる復元**: 欠落しているテクスチャが別のパッケージに分割されている場合、コンパニオンパッケージの取り込みによって自動復元されます。
- **プリセット切り替え**: ステーションのタブレットプレビューから、Automatic 変換、明示的な Xiexe Toon、または PBS Metallic をいつでも手動で切り替えることができます。
