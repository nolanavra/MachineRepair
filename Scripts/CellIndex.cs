using System.Runtime.CompilerServices;

namespace MachineRepair.Grid {
    public static class CellIndex {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToIndex(int x, int y, int width) => y * width + x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int x, int y) FromIndex(int index, int width) => (index % width, index / width);
    }
}
