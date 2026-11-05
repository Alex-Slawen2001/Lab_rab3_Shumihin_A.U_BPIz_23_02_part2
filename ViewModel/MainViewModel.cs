// ==========================================
// Файл: ViewModel/MainViewModel.cs
// Назначение: ViewModel главного окна (вариант Task + async/await).
//             Команды — AsyncRelayCommand из CommunityToolkit.Mvvm.Input,
//             создаются явно в конструкторе (метод команды возвращает Task).
//             После await выполнение возвращается в UI-поток, поэтому свойства
//             можно менять напрямую, без SynchronizationContext.
//
//             Задания для самостоятельного выполнения:
//               1) HeapSortCommand — пирамидальная сортировка;
//               2) CancelAllCommand — «Отменить всё» (CancellationTokenSource);
//               3) BubbleProgress ... HeapProgress — прогресс через IProgress<double>;
//               4) RunAllParallelCommand / RunAllSequentialCommand — замер общего
//                  времени при одновременном и последовательном запуске,
//                  результаты накапливаются в MeasurementLog.
// ==========================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lab_rab3_Shumihin_A_U_BPIz_23_02_part_2.Model;

namespace Lab_rab3_Shumihin_A_U_BPIz_23_02_part_2.ViewModel
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ArraySorter _sorter;
        private int[] _originalArray;

        // источник токенов отмены (задание 2)
        private CancellationTokenSource _cts;

        // число выполняющихся сортировок и признак пакетного запуска (задание 4)
        private int _activeSorts;
        private bool _batchRunning;

        // общий токен отмены для пакетного запуска: «Отменить всё» отменяет
        // и текущую, и ещё не начатые сортировки последовательного запуска
        private CancellationToken _batchToken;

        // время последнего выполнения каждой сортировки, мс (null — отменена)
        private readonly Dictionary<string, double?> _lastElapsed = new Dictionary<string, double?>();

        // ---------------- Наблюдаемые свойства ----------------

        [ObservableProperty]
        private int _arraySize = 1000;

        [ObservableProperty]
        private string _originalArrayString;

        [ObservableProperty]
        private string _bubbleSortResult;

        [ObservableProperty]
        private string _quickSortResult;

        [ObservableProperty]
        private string _insertionSortResult;

        [ObservableProperty]
        private string _heapSortResult;

        [ObservableProperty]
        private string _totalComparisons = "Общее число сравнений: 0";

        // прогресс сортировок, % (задание 3)
        [ObservableProperty] private double _bubbleProgress;
        [ObservableProperty] private double _quickProgress;
        [ObservableProperty] private double _insertionProgress;
        [ObservableProperty] private double _heapProgress;

        // нет выполняющихся сортировок
        [ObservableProperty]
        private bool _isIdle = true;

        /// <summary>Журнал замеров времени (задание 4)</summary>
        public ObservableCollection<string> MeasurementLog { get; } = new ObservableCollection<string>();

        // ---------------- Команды ----------------

        public IAsyncRelayCommand GenerateArrayCommand { get; }
        public IAsyncRelayCommand BubbleSortCommand { get; }
        public IAsyncRelayCommand QuickSortCommand { get; }
        public IAsyncRelayCommand InsertionSortCommand { get; }
        public IAsyncRelayCommand HeapSortCommand { get; }
        public IAsyncRelayCommand RunAllParallelCommand { get; }
        public IAsyncRelayCommand RunAllSequentialCommand { get; }
        public IRelayCommand CancelAllCommand { get; }

        public MainViewModel()
        {
            _sorter = new ArraySorter();

            // Инициализация команд
            GenerateArrayCommand    = new AsyncRelayCommand(GenerateArrayAsync, CanGenerateArray);
            BubbleSortCommand       = new AsyncRelayCommand(BubbleSortAsync, CanSortBubble);
            QuickSortCommand        = new AsyncRelayCommand(QuickSortAsync, CanSortQuick);
            InsertionSortCommand    = new AsyncRelayCommand(InsertionSortAsync, CanSortInsertion);
            HeapSortCommand         = new AsyncRelayCommand(HeapSortAsync, CanSortHeap);
            RunAllParallelCommand   = new AsyncRelayCommand(RunAllParallelAsync, CanRunAll);
            RunAllSequentialCommand = new AsyncRelayCommand(RunAllSequentialAsync, CanRunAll);
            CancelAllCommand        = new RelayCommand(CancelAll, CanCancelAll);
        }

        // ---------------- Условия выполнения команд ----------------

        private bool CanGenerateArray() => !GenerateArrayCommand.IsRunning && _activeSorts == 0;
        private bool CanSortBubble()    => _originalArray != null && !BubbleSortCommand.IsRunning    && !_batchRunning;
        private bool CanSortQuick()     => _originalArray != null && !QuickSortCommand.IsRunning     && !_batchRunning;
        private bool CanSortInsertion() => _originalArray != null && !InsertionSortCommand.IsRunning && !_batchRunning;
        private bool CanSortHeap()      => _originalArray != null && !HeapSortCommand.IsRunning      && !_batchRunning;
        private bool CanRunAll()        => _originalArray != null && _activeSorts == 0;
        private bool CanCancelAll()     => _activeSorts > 0;

        // ---------------- Асинхронные методы команд ----------------

        private async Task GenerateArrayAsync()
        {
            if (ArraySize < 2 || ArraySize > 1_000_000)
            {
                MessageBox.Show("Размер массива должен быть от 2 до 1 000 000.", "Предупреждение");
                return;
            }

            // Имитация небольшой задержки (кнопка на это время блокируется)
            await Task.Delay(100);

            _originalArray = _sorter.GenerateRandomArray(ArraySize);
            OriginalArrayString = "Исходный массив: " +
                string.Join(", ", _originalArray.Take(20)) +
                (ArraySize > 20 ? "..." : "");

            // Сброс результатов
            BubbleSortResult = QuickSortResult = InsertionSortResult = HeapSortResult = null;
            BubbleProgress = QuickProgress = InsertionProgress = HeapProgress = 0;
            _sorter.ResetTotalComparisons();
            TotalComparisons = "Общее число сравнений: 0";

            // Обновляем состояние других команд
            NotifyCommands();
        }

        private async Task BubbleSortAsync()
        {
            BubbleSortResult = "Сортируется...";
            BeginSort();
            try
            {
                // IProgress<double>: Progress создан в UI-потоке, поэтому обработчик
                // выполняется в UI-потоке и может менять свойство напрямую
                var progress = new Progress<double>(p => BubbleProgress = p);

                // Запускаем асинхронную сортировку и ждём результат
                var result = await _sorter.BubbleSortAsync(_originalArray, progress, GetToken());

                if (result.Cancelled)
                {
                    BubbleSortResult = "Пузырьковая: отменено";
                    _lastElapsed["Пузырьковая"] = null;
                    return;
                }

                BubbleSortResult = $"Пузырьковая: {FormatArray(result.SortedArray)}, " +
                                   $"время: {result.ElapsedMilliseconds:F2} мс, сравнений: {result.Comparisons}";
                _lastElapsed["Пузырьковая"] = result.ElapsedMilliseconds;
                UpdateTotalComparisons();
            }
            catch (OperationCanceledException)
            {
                BubbleSortResult = "Пузырьковая: отменено";
                _lastElapsed["Пузырьковая"] = null;
            }
            finally
            {
                EndSort();
            }
        }

        private async Task QuickSortAsync()
        {
            QuickSortResult = "Сортируется...";
            BeginSort();
            try
            {
                var progress = new Progress<double>(p => QuickProgress = p);
                var result = await _sorter.QuickSortAsync(_originalArray, progress, GetToken());

                if (result.Cancelled)
                {
                    QuickSortResult = "Быстрая: отменено";
                    _lastElapsed["Быстрая"] = null;
                    return;
                }

                QuickSortResult = $"Быстрая: {FormatArray(result.SortedArray)}, " +
                                  $"время: {result.ElapsedMilliseconds:F2} мс, сравнений: {result.Comparisons}";
                _lastElapsed["Быстрая"] = result.ElapsedMilliseconds;
                UpdateTotalComparisons();
            }
            catch (OperationCanceledException)
            {
                QuickSortResult = "Быстрая: отменено";
                _lastElapsed["Быстрая"] = null;
            }
            finally
            {
                EndSort();
            }
        }

        private async Task InsertionSortAsync()
        {
            InsertionSortResult = "Сортируется...";
            BeginSort();
            try
            {
                var progress = new Progress<double>(p => InsertionProgress = p);
                var result = await _sorter.InsertionSortAsync(_originalArray, progress, GetToken());

                if (result.Cancelled)
                {
                    InsertionSortResult = "Вставками: отменено";
                    _lastElapsed["Вставками"] = null;
                    return;
                }

                InsertionSortResult = $"Вставками: {FormatArray(result.SortedArray)}, " +
                                      $"время: {result.ElapsedMilliseconds:F2} мс, сравнений: {result.Comparisons}";
                _lastElapsed["Вставками"] = result.ElapsedMilliseconds;
                UpdateTotalComparisons();
            }
            catch (OperationCanceledException)
            {
                InsertionSortResult = "Вставками: отменено";
                _lastElapsed["Вставками"] = null;
            }
            finally
            {
                EndSort();
            }
        }

        // Пирамидальная сортировка (задание 1)
        private async Task HeapSortAsync()
        {
            HeapSortResult = "Сортируется...";
            BeginSort();
            try
            {
                var progress = new Progress<double>(p => HeapProgress = p);
                var result = await _sorter.HeapSortAsync(_originalArray, progress, GetToken());

                if (result.Cancelled)
                {
                    HeapSortResult = "Пирамидальная: отменено";
                    _lastElapsed["Пирамидальная"] = null;
                    return;
                }

                HeapSortResult = $"Пирамидальная: {FormatArray(result.SortedArray)}, " +
                                 $"время: {result.ElapsedMilliseconds:F2} мс, сравнений: {result.Comparisons}";
                _lastElapsed["Пирамидальная"] = result.ElapsedMilliseconds;
                UpdateTotalComparisons();
            }
            catch (OperationCanceledException)
            {
                HeapSortResult = "Пирамидальная: отменено";
                _lastElapsed["Пирамидальная"] = null;
            }
            finally
            {
                EndSort();
            }
        }

        // ---------------- Задание 4: одновременный и последовательный запуск ----------------

        /// <summary>Все четыре сортировки одновременно: Task.WhenAll</summary>
        private async Task RunAllParallelAsync()
        {
            BeginBatch();

            var watch = Stopwatch.StartNew();
            await Task.WhenAll(BubbleSortAsync(), QuickSortAsync(), InsertionSortAsync(), HeapSortAsync());
            watch.Stop();

            _batchRunning = false;
            AddMeasurement("одновременно (Task.WhenAll)", watch.Elapsed.TotalMilliseconds);
            NotifyCommands();
        }

        /// <summary>Все четыре сортировки по очереди: каждая следующая — после await предыдущей</summary>
        private async Task RunAllSequentialAsync()
        {
            BeginBatch();

            var watch = Stopwatch.StartNew();
            await BubbleSortAsync();
            await QuickSortAsync();
            await InsertionSortAsync();
            await HeapSortAsync();
            watch.Stop();

            _batchRunning = false;
            AddMeasurement("последовательно", watch.Elapsed.TotalMilliseconds);
            NotifyCommands();
        }

        private void AddMeasurement(string mode, double totalMs)
        {
            string line;
            if (_lastElapsed.Values.Any(v => v == null))
            {
                line = $"n = {_originalArray.Length}: {mode} — прервано через {totalMs:F0} мс";
            }
            else
            {
                string details = string.Join(", ", _lastElapsed.Select(p => $"{p.Key} {p.Value:F0}"));
                double sum = _lastElapsed.Values.Sum(v => v.Value);
                line = $"n = {_originalArray.Length}: {mode} — общее время {totalMs:F0} мс " +
                       $"(сумма времён сортировок {sum:F0} мс: {details})";
            }
            MeasurementLog.Insert(0, line);
        }

        // ---------------- Задание 2: отмена всех сортировок ----------------

        private void CancelAll()
        {
            _cts?.Cancel();
        }

        /// <summary>Токен отмены; после отмены создаётся новый источник</summary>
        private CancellationToken GetToken()
        {
            if (_batchRunning)
                return _batchToken;
            if (_cts == null || _cts.IsCancellationRequested)
                _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        /// <summary>Начало пакетного запуска: один токен отмены на все сортировки</summary>
        private void BeginBatch()
        {
            if (_cts == null || _cts.IsCancellationRequested)
                _cts = new CancellationTokenSource();
            _batchToken = _cts.Token;
            _batchRunning = true;
            _lastElapsed.Clear();
            NotifyCommands();
        }

        // ---------------- Вспомогательные методы ----------------

        private void BeginSort()
        {
            _activeSorts++;
            NotifyCommands();
        }

        private void EndSort()
        {
            _activeSorts--;
            NotifyCommands();
        }

        /// <summary>Обновление доступности всех команд</summary>
        private void NotifyCommands()
        {
            IsIdle = _activeSorts == 0;
            GenerateArrayCommand.NotifyCanExecuteChanged();
            BubbleSortCommand.NotifyCanExecuteChanged();
            QuickSortCommand.NotifyCanExecuteChanged();
            InsertionSortCommand.NotifyCanExecuteChanged();
            HeapSortCommand.NotifyCanExecuteChanged();
            RunAllParallelCommand.NotifyCanExecuteChanged();
            RunAllSequentialCommand.NotifyCanExecuteChanged();
            CancelAllCommand.NotifyCanExecuteChanged();
        }

        private void UpdateTotalComparisons()
        {
            TotalComparisons = $"Общее число сравнений: {_sorter.TotalComparisons}";
        }

        private string FormatArray(int[] arr)
        {
            // string.Join(", ", arr, 0, 5) для int[] не подходит: такая перегрузка есть
            // только для string[], поэтому берём первые элементы через LINQ Take
            if (arr.Length <= 10)
                return string.Join(", ", arr);
            else
                return string.Join(", ", arr.Take(5)) + "...";
        }
    }
}
