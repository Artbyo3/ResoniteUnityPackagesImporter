#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

/// <summary>
/// Evaluates ownership of a target avatar to ensure operations do not mutate
/// another user's avatar, temporary import staging slots, or cross-world objects.
/// </summary>
internal static class AvatarOwnershipValidator
{
    public static bool EvaluateOwnership<TUser>(
        bool isRigValid,
        bool isRigDestroyed,
        bool isTemporaryStaging,
        bool isSameWorld,
        TUser? localUser,
        TUser? activeUser,
        TUser? allocatingUser,
        IReadOnlyList<TUser?> protectionUsers) where TUser : class
    {
        if (!isRigValid || isRigDestroyed || localUser == null || !isSameWorld || isTemporaryStaging)
            return false;

        // Any protection component that targets a user other than the local user blocks access.
        if (protectionUsers.Any(owner => owner != null && !ReferenceEquals(owner, localUser)))
            return false;

        // If someone is actively wearing this avatar, they must be the local user.
        if (activeUser != null)
            return ReferenceEquals(activeUser, localUser);

        // If explicit protections exist and target the local user, local user owns it.
        if (protectionUsers.Any(owner => ReferenceEquals(owner, localUser)))
            return true;

        // Otherwise, the avatar must have been allocated by the local user.
        return ReferenceEquals(allocatingUser, localUser);
    }
}
