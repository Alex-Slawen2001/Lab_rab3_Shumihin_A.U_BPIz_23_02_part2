// ==========================================
// Файл: MainWindow.xaml.cs
// Назначение: Code-behind главного окна — только установка DataContext.
// ==========================================

using System.Windows;
using Lab_rab3_Shumihin_A_U_BPIz_23_02_part_2.ViewModel;

namespace Lab_rab3_Shumihin_A_U_BPIz_23_02_part_2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}
