namespace Qvd;

internal static class BitPacking
{
    /// <summary>
    /// Extracts an unsigned bit field from a bit-packed record. QVD packs bits
    /// little-endian: bit k of the record is bit (k mod 8) of byte (k div 8).
    /// </summary>
    internal static int Extract(ReadOnlySpan<byte> record, int bitOffset, int bitWidth)
    {
        if (bitWidth == 0)
        {
            return 0;
        }

        // Accumulate the (up to 5) bytes covering the bit range, then shift and mask.
        int firstByte = bitOffset >> 3;
        int lastByte = (bitOffset + bitWidth - 1) >> 3;

        ulong accumulator = 0;
        for (int b = lastByte; b >= firstByte; b--)
        {
            accumulator = (accumulator << 8) | record[b];
        }

        accumulator >>= bitOffset & 7;
        return (int)(accumulator & ((1UL << bitWidth) - 1));
    }
}
