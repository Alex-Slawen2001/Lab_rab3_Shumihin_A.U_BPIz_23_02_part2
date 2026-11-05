// ==========================================
// Файл: Model/SortResult.cs
// Назначение: Результат асинхронной сортировки, возвращаемый из Task:
//             отсортированный массив, число сравнений и время выполнения;
//             признак Cancelled — сортировка отменена.
// ==========================================

namespace Lab_rab3_Shumihin_A_U_BPIz_23_02_part_2.Model
{
    public class SortResult
    {
        /// <summary>отсортированный массив</summary>
        public int[] SortedArray { get; set; }

        /// <summary>количество сравнений</summary>
        public long Comparisons { get; set; }

        /// <summary>время сортировки, мс</summary>
        public double ElapsedMilliseconds { get; set; }

        /// <summary>сортировка была отменена через CancellationToken</summary>
        public bool Cancelled { get; set; }
    }
}
