using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;

namespace UnityPackageImporter.Models;

public static class ContextMenuBuilder
{
    public static async Task<bool> BuildExpressionsMenuAsync(
        Slot avatarRoot,
        VrcMenu rootMenu,
        IReadOnlyDictionary<VrcMenuControl, IField<bool>> bindings,
        UnityProjectImporter importer)
    {
        if (avatarRoot == null || rootMenu == null || rootMenu.Controls.Count == 0) return true;

        UnityPackageImporter.Msg($"Building native Expressions Menu for '{avatarRoot.Name}' ({rootMenu.Controls.Count} root items)");

        await default(ToWorld);

        // 1. Create a dedicated container slot for the menu hierarchy under avatar root
        var menuRootSlot = avatarRoot.AddSlot("Expressions Menu");
        menuRootSlot.OrderOffset = 1000;

        // 2. Setup ContextMenuItemSource for the root
        var rootItemSource = menuRootSlot.AttachComponent<ContextMenuItemSource>();
        rootItemSource.Label.Value = string.IsNullOrEmpty(rootMenu.Name) ? "Avatar Menu" : rootMenu.Name;
        rootItemSource.Color.Value = new colorX(0.45f, 0.5f, 0.95f, 1f);

        // Keep the menu owned by this descriptor's avatar root. Sharing it with
        // prefab/import ancestors makes multi-avatar packages leak menus between avatars.
        var rootComp1 = avatarRoot.AttachComponent<RootContextMenuItem>();
        rootComp1.Item.Target = rootItemSource;

        // 3. Setup root submenu container
        var rootSubmenu = menuRootSlot.AttachComponent<ContextMenuSubmenu>();
        var rootItemsSlot = menuRootSlot.AddSlot("Items");
        rootSubmenu.ItemsRoot.Target = rootItemsSlot;

        // 4. Build controls recursively
        bool complete = await BuildMenuLevelAsync(rootMenu, rootItemsSlot, bindings, importer);

        UnityPackageImporter.Msg($"Built Expressions Menu on '{avatarRoot.Name}'; all controls bound: {complete}");
        return complete;
    }

    private static async Task<bool> BuildMenuLevelAsync(
        VrcMenu menu,
        Slot parentItemsSlot,
        IReadOnlyDictionary<VrcMenuControl, IField<bool>> bindings,
        UnityProjectImporter importer)
    {
        bool complete = true;
        foreach (var control in menu.Controls)
        {
            if (control.IsSubMenu && control.SubMenu != null)
            {
                var subSlot = parentItemsSlot.AddSlot(control.Name);
                var subItemSource = subSlot.AttachComponent<ContextMenuItemSource>();
                subItemSource.Label.Value = control.Name;
                subItemSource.Color.Value = new colorX(0.35f, 0.65f, 0.85f, 1f);

                // Assign icon if available
                if (!string.IsNullOrEmpty(control.IconGuid))
                {
                    await AttachMenuIconAsync(subSlot, subItemSource, control.IconGuid, importer);
                }

                var submenuComp = subSlot.AttachComponent<ContextMenuSubmenu>();
                var subItemsSlot = subSlot.AddSlot("Items");
                submenuComp.ItemsRoot.Target = subItemsSlot;

                complete &= await BuildMenuLevelAsync(control.SubMenu, subItemsSlot, bindings, importer);
            }
            else
            {
                var itemSlot = parentItemsSlot.AddSlot(control.Name);
                var itemSource = itemSlot.AttachComponent<ContextMenuItemSource>();
                itemSource.Label.Value = control.Name;
                itemSource.Color.Value = new colorX(0.85f, 0.9f, 0.95f, 1f);

                // Assign icon if available
                if (!string.IsNullOrEmpty(control.IconGuid))
                {
                    await AttachMenuIconAsync(itemSlot, itemSource, control.IconGuid, importer);
                }

                if (control.IsToggle && bindings.TryGetValue(control, out var parameter))
                {
                    itemSlot.AttachComponent<ButtonToggle>().TargetValue.Target = parameter;
                }
                else
                {
                    itemSource.ButtonEnabled.Value = false;
                    complete = false;
                    UnityPackageImporter.Warn("Expression control '" + control.Name +
                        "' is unavailable: no exact supported runtime binding. Clothing names are not used to guess Animator behavior.");
                }
            }
        }
        return complete;
    }

    internal static async Task AttachMenuIconAsync(
        Slot itemSlot,
        ContextMenuItemSource itemSource,
        string iconGuid,
        UnityProjectImporter importer)
    {
        if (importer.AssetIDDict.TryGetValue(iconGuid, out var iconPath) && File.Exists(iconPath))
        {
            try
            {
                var texture = await importer.TextureImports.GetOrAdd(iconGuid, async () =>
                {
                    await default(ToBackground);
                    var url = await importer.world.Engine.LocalDB.ImportLocalAssetAsync(iconPath, LocalDB.ImportLocation.Copy);
                    await default(ToWorld);
                    var texSlot = importer.importTaskAssetRoot.AddSlot(Path.GetFileName(iconPath) + " [" + iconGuid + "] - Icon");
                    var tex = texSlot.AttachComponent<StaticTexture2D>();
                    tex.URL.Value = url;
                    return tex;
                });

                if (texture != null)
                {
                    await default(ToWorld);
                    var spriteProvider = itemSlot.AttachComponent<SpriteProvider>();
                    spriteProvider.Texture.Target = texture;
                    itemSource.Sprite.Target = spriteProvider;
                }
            }
            catch (Exception ex)
            {
                UnityPackageImporter.Warn($"Failed to load menu icon '{iconPath}': {ex.Message}");
            }
        }
    }
}
