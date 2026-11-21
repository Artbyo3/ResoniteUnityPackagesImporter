#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;





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


        if (protectionUsers.Any(owner => owner != null && !ReferenceEquals(owner, localUser)))
            return false;


        if (activeUser != null)
            return ReferenceEquals(activeUser, localUser);


        if (protectionUsers.Any(owner => ReferenceEquals(owner, localUser)))
            return true;


        return ReferenceEquals(allocatingUser, localUser);
    }
}
