// ==========================================
// Файл: Model/ArraySorter.cs
// Назначение: Модель — генерация массива и алгоритмы сортировки (вариант с Task).
//             Методы BubbleSortAsync, QuickSortAsync, InsertionSortAsync, HeapSortAsync
//             запускают сортировку в пуле потоков через Task.Run и возвращают
//             Task<SortResult> — результат получается через await, без событий
//             и без SynchronizationContext.
//             Общий счётчик сравнений защищён lock (несколько Task могут
//             завершиться одновременно).
//             Параметры IProgress<double> и CancellationToken — для индикации
//             прогресса и отмены (задания для самостоятельного выполнения).
// ==========================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lab_rab3_Shumihin_A_U_BPIz_23_02_part_2.Model
{
    public class ArraySorter
    {
        // Общий счётчик сравнений (разделяемый ресурс)
        private long _totalComparisons;
        private readonly object _locker = new object();

        public long TotalComparisons
        {
            get { lock (_locker) { return _totalComparisons; } }
        }

        /// <summary>Сброс общего счётчика (при генерации нового массива)</summary>
        public void ResetTotalComparisons()
        {
            lock (_locker) { _totalComparisons = 0; }
        }

        // Генерация случайного массива заданного размера
        public int[] GenerateRandomArray(int size)
        {
            Random rand = new Random();
            int[] array = new int[size];
            for (int i = 0; i < size; i++)
                array[i] = rand.Next(1000); // числа от 0 до 999
            return array;
        }

        // Копирование массива (каждая задача работает со своей копией)
        private int[] CopyArray(int[] source)
        {
            int[] copy = new int[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        // ---------------- Асинхронные методы ----------------

        public Task<SortResult> BubbleSortAsync(int[] originalArray,
            IProgress<double> progress = null, CancellationToken token = default)
        {
            return Task.Run(() => Run(originalArray, progress, token, BubbleCore), token);
        }

        public Task<SortResult> QuickSortAsync(int[] originalArray,
            IProgress<double> progress = null, CancellationToken token = default)
        {
            return Task.Run(() => Run(originalArray, progress, token, QuickCore), token);
        }

        public Task<SortResult> InsertionSortAsync(int[] originalArray,
            IProgress<double> progress = null, CancellationToken token = default)
        {
            return Task.Run(() => Run(originalArray, progress, token, InsertionCore), token);
        }

        // Пирамидальная сортировка (задание 1)
        public Task<SortResult> HeapSortAsync(int[] originalArray,
            IProgress<double> progress = null, CancellationToken token = default)
        {
            return Task.Run(() => Run(originalArray, progress, token, HeapCore), token);
        }

        /// <summary>
        /// Выполнение сортировки внутри задачи: копия массива → замер времени →
        /// алгоритм → потокобезопасное увеличение общего счётчика → результат.
        /// При отмене алгоритм выбрасывает OperationCanceledException,
        /// он перехватывается здесь, и возвращается результат с Cancelled = true.
        /// </summary>
        private SortResult Run(int[] originalArray, IProgress<double> progress, CancellationToken token,
                               Func<int[], CancellationToken, Action<double>, long> algorithm)
        {
            int[] array = CopyArray(originalArray);
            var watch = Stopwatch.StartNew();

            long comparisons;
            try
            {
                comparisons = algorithm(array, token, CreateReporter(progress));
            }
            catch (OperationCanceledException)
            {
                // Отмену обрабатываем здесь, внутри своего кода, и возвращаем признак Cancelled.
                // Если исключение «вылетит» из Task.Run, отладчик Visual Studio
                // (режим «Только мой код») остановит программу, как будто она зависла.
                return new SortResult { Cancelled = true };
            }

            watch.Stop();
            lock (_locker)
            {
                _totalComparisons += comparisons;
            }

            return new SortResult
            {
                SortedArray = array,
                Comparisons = comparisons,
                ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds
            };
        }

        /// <summary>
        /// Прогресс передаётся в IProgress только при изменении целого процента —
        /// частые обновления из нескольких потоков замедлили бы работу.
        /// </summary>
        private static Action<double> CreateReporter(IProgress<double> progress)
        {
            if (progress == null) return _ => { };
            int last = -1;
            return fraction =>
            {
                int percent = (int)(fraction * 100);
                if (percent != last)
                {
                    last = percent;
                    progress.Report(percent);
                }
            };
        }

        // ---------------- Алгоритмы ----------------

        // Пузырьковая сортировка — O(n²)
        private long BubbleCore(int[] array, CancellationToken token, Action<double> report)
        {
            long comparisons = 0;
            for (int i = 0; i < array.Length - 1; i++)
            {
                token.ThrowIfCancellationRequested();
                for (int j = 0; j < array.Length - 1 - i; j++)
                {
                    comparisons++;
                    if (array[j] > array[j + 1])
                    {
                        int temp = array[j];
                        array[j] = array[j + 1];
                        array[j + 1] = temp;
                    }
                }
                report((double)(i + 1) / (array.Length - 1));
            }
            report(1);
            return comparisons;
        }

        // Быстрая сортировка — O(n log n)
        private long QuickCore(int[] array, CancellationToken token, Action<double> report)
        {
            long comparisons = 0;
            int done = 0;
            QuickSortRecursive(array, 0, array.Length - 1, ref comparisons, ref done, token, report);
            report(1);
            return comparisons;
        }

        private void QuickSortRecursive(int[] arr, int left, int right, ref long comparisons,
                                        ref int done, CancellationToken token, Action<double> report)
        {
            token.ThrowIfCancellationRequested();
            if (left < right)
            {
                int pivotIndex = Partition(arr, left, right, ref comparisons);
                done++;
                report((double)done / arr.Length);
                QuickSortRecursive(arr, left, pivotIndex - 1, ref comparisons, ref done, token, report);
                QuickSortRecursive(arr, pivotIndex + 1, right, ref comparisons, ref done, token, report);
            }
            else if (left == right)
            {
                done++;
                report((double)done / arr.Length);
            }
        }

        private int Partition(int[] arr, int left, int right, ref long comparisons)
        {
            int pivot = arr[right];
            int i = left - 1;
            for (int j = left; j < right; j++)
            {
                comparisons++;
                if (arr[j] < pivot)
                {
                    i++;
                    int temp = arr[i];
                    arr[i] = arr[j];
                    arr[j] = temp;
                }
            }
            int temp1 = arr[i + 1];
            arr[i + 1] = arr[right];
            arr[right] = temp1;
            return i + 1;
        }

        // Сортировка вставками — O(n²) в худшем случае
        private long InsertionCore(int[] array, CancellationToken token, Action<double> report)
        {
            long comparisons = 0;
            for (int i = 1; i < array.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int key = array[i];
                int j = i - 1;
                while (j >= 0 && array[j] > key)
                {
                    comparisons++;
                    array[j + 1] = array[j];
                    j--;
                }
                comparisons++; // учёт последнего сравнения, когда условие не выполнено
                array[j + 1] = key;
                report((double)(i + 1) / array.Length);
            }
            report(1);
            return comparisons;
        }

        // Пирамидальная сортировка — O(n log n):
        // строим max-кучу, затем n-1 раз переносим максимум в конец и восстанавливаем кучу
        private long HeapCore(int[] array, CancellationToken token, Action<double> report)
        {
            long comparisons = 0;
            int n = array.Length;

            for (int i = n / 2 - 1; i >= 0; i--)
                Heapify(array, n, i, ref comparisons);

            for (int end = n - 1; end > 0; end--)
            {
                token.ThrowIfCancellationRequested();
                int temp = array[0];
                array[0] = array[end];
                array[end] = temp;
                Heapify(array, end, 0, ref comparisons);
                report((double)(n - end) / n);
            }
            report(1);
            return comparisons;
        }

        // Просеивание элемента i вниз в куче размера size
        private void Heapify(int[] arr, int size, int i, ref long comparisons)
        {
            while (true)
            {
                int largest = i;
                int left = 2 * i + 1;
                int right = 2 * i + 2;

                if (left < size)
                {
                    comparisons++;
                    if (arr[left] > arr[largest]) largest = left;
                }
                if (right < size)
                {
                    comparisons++;
                    if (arr[right] > arr[largest]) largest = right;
                }
                if (largest == i) return;

                int temp = arr[i];
                arr[i] = arr[largest];
                arr[largest] = temp;
                i = largest;
            }
        }
    }
}
