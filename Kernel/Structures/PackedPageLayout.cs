using System;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace CoreECS.Structures
{
    internal static class PackedPageLayout
    {
        private const int FirstPageShift = 2;
        private const int LargestPage = 28;

        internal static int PageSize(int page)
        {
            if (page < 0 || page > LargestPage)
            {
                throw new ArgumentOutOfRangeException(nameof(page));
            }

            return 1 << (page + FirstPageShift);
        }

        internal static int CapacityBeforePage(int page)
        {
            if (page < 0 || page > LargestPage + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(page));
            }

            return (int)((1U << (page + FirstPageShift)) - (1U << FirstPageShift));
        }

        internal static void Locate(int slot, out int page, out int offset)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            page = HighestBit((uint)slot + (1U << FirstPageShift)) - FirstPageShift;
            offset = slot - CapacityBeforePage(page);
        }

        private static int HighestBit(uint value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.Log2(value);
#else
            int bit = 0;
            if (value >= 1U << 16)
            {
                value >>= 16;
                bit += 16;
            }
            if (value >= 1U << 8)
            {
                value >>= 8;
                bit += 8;
            }
            if (value >= 1U << 4)
            {
                value >>= 4;
                bit += 4;
            }
            if (value >= 1U << 2)
            {
                value >>= 2;
                bit += 2;
            }
            if (value >= 1U << 1)
            {
                bit += 1;
            }

            return bit;
#endif
        }
    }
}
