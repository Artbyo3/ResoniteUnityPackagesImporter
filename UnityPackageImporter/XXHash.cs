// <copyright file="XXHash.cs" company="Sedat Kapanoglu">
// Copyright (c) 2015-2022 Sedat Kapanoglu
// MIT License (see LICENSE file for details)
// </copyright>

using System;
using System.Runtime.CompilerServices;
using Elements.Core;
namespace HashDepot;

/// <summary>
/// XXHash implementation.
/// </summary>
public static class XXHash
{
    private const ulong prime64v1 = 11400714785074694791ul;
    private const ulong prime64v2 = 14029467366897019727ul;
    private const ulong prime64v3 = 1609587929392839161ul;
    private const ulong prime64v4 = 9650029242287828579ul;
    private const ulong prime64v5 = 2870177450012600261ul;


    /// <summary>
    /// Generate a 64-bit xxHash value.
    /// </summary>
    /// <param name="buffer">Input buffer.</param>
    /// <param name="seed">Optional seed.</param>
    /// <returns>Computed 64-bit hash value.</returns>
    public static ulong Hash64(ReadOnlySpan<byte> buffer, ulong seed = 0)
    {
        const int stripeLength = 32;

        int evenLength = buffer.Length - (buffer.Length % stripeLength);
        ulong acc;

        int offset = 0;
        if (buffer.Length < stripeLength)
        {
            acc = seed + prime64v5;
            goto Exit;
        }

        var (acc1, acc2, acc3, acc4) = initAccumulators64(seed);
        do
        {
            int end = offset + stripeLength;
            acc = processStripe64(buffer.Slice(offset, stripeLength), ref acc1, ref acc2, ref acc3, ref acc4);
            offset = end;
        }
        while (offset < evenLength);

    Exit:
        acc += (ulong)buffer.Length;
        acc = processRemaining64(buffer.Slice(offset), acc);
        return avalanche64(acc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (ulong Acc1, ulong Acc2, ulong Acc3, ulong Acc4) initAccumulators64(ulong seed)
    {
        return (seed + prime64v1 + prime64v2, seed + prime64v2, seed, seed - prime64v1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong processStripe64(
        ReadOnlySpan<byte> buf,
        ref ulong acc1,
        ref ulong acc2,
        ref ulong acc3,
        ref ulong acc4)
    {
        processLane64(ref acc1, buf.Slice(0,8));
        processLane64(ref acc2, buf.Slice(8,8));
        processLane64(ref acc3, buf.Slice(16, 8));
        processLane64(ref acc4, buf.Slice(24, 8));

        ulong acc = MathX.RotateLeft(acc1, 1)
                  + MathX.RotateLeft(acc2, 7)
                  + MathX.RotateLeft(acc3, 12)
                  + MathX.RotateLeft(acc4, 18);

        mergeAccumulator64(ref acc, acc1);
        mergeAccumulator64(ref acc, acc2);
        mergeAccumulator64(ref acc, acc3);
        mergeAccumulator64(ref acc, acc4);
        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void processLane64(ref ulong accn, ReadOnlySpan<byte> buf)
    {
        ulong lane = BitConverter.ToUInt64(buf.ToArray(),0);
        accn = round64(accn, lane);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong processRemaining64(
        ReadOnlySpan<byte> remaining,
        ulong acc)
    {
        int remainingLen = remaining.Length;
        int i = 0;
        for (ulong lane; remainingLen >= 8; remainingLen -= 8, i += 8)
        {
            lane = BitConverter.ToUInt64(remaining.Slice(i, 8).ToArray(), 0);
            acc ^= round64(0, lane);
            acc = MathX.RotateLeft(acc, 27) * prime64v1;
            acc += prime64v4;
        }

        for (uint lane32; remainingLen >= 4; remainingLen -= 4, i += 4)
        {
            lane32 = BitConverter.ToUInt32(remaining.Slice(i, 4).ToArray(),0);
            acc ^= lane32 * prime64v1;
            acc = MathX.RotateLeft(acc, 23) * prime64v2;
            acc += prime64v3;
        }

        for (byte lane8; remainingLen >= 1; remainingLen--, i++)
        {
            lane8 = remaining[i];
            acc ^= lane8 * prime64v5;
            acc = MathX.RotateLeft(acc, 11) * prime64v1;
        }

        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong avalanche64(ulong acc)
    {
        acc ^= acc >> 33;
        acc *= prime64v2;
        acc ^= acc >> 29;
        acc *= prime64v3;
        acc ^= acc >> 32;
        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong round64(ulong accn, ulong lane)
    {
        accn += lane * prime64v2;
        return MathX.RotateLeft(accn, 31) * prime64v1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void mergeAccumulator64(ref ulong acc, ulong accn)
    {
        acc ^= round64(0, accn);
        acc *= prime64v1;
        acc += prime64v4;
    }

}
