# Modular Avatar

**対応する衣装構造、独立した衣装切り替え、継続的なブレンドシェイプ同期を変換します。複雑なコントローラーやセッションをまたぐ状態保存は再現しません。** コンポーネントが存在しても Unity のビルド処理全体が Resonite で実行されるわけではありません。

Merge Armature と Bone Proxy は対応する構造操作へ変換します。独立した二値の衣装操作には `ModularAvatarMenuItem` の SubMenu と Toggle、`ModularAvatarMenuInstaller`、`ModularAvatarMenuGroup`、型付き定義を持つ `ModularAvatarParameters` を使用します。

対応する表示切り替え（`ModularAvatarObjectToggle`）、ブレンドシェイプ（`ModularAvatarShapeChanger` の Set モード）、マテリアルスロット（`m_inverted` を含む `ModularAvatarMaterialSetter`）は、無効にしたときに元の基準状態へ戻します。`ModularAvatarBlendshapeSync` の継続的な同期にはネイティブの `ValueCopy<float>` を使用します。

任意の多層 Animator コントローラー、パペット操作、破壊的な切断、セッションをまたぐ状態保存は変換されません。各コンポーネントの範囲と制限を確認してください。ソース確認や自動テストは実機での外観や動作の確認とは別です。
