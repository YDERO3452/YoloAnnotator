using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;

namespace YoloAnnotator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var errorMsg = $"未处理的异常: {ex?.Message}\n\n堆栈跟踪:\n{ex?.StackTrace}";
        
        // 写入日志
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            File.AppendAllText(logPath, $"{DateTime.Now}: {errorMsg}\n\n");
        }
        catch { }

        MessageBox.Show(errorMsg, "程序崩溃", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var errorMsg = $"UI线程异常: {e.Exception.Message}\n\n堆栈跟踪:\n{e.Exception.StackTrace}";
        
        // 写入日志
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            File.AppendAllText(logPath, $"{DateTime.Now}: {errorMsg}\n\n");
        }
        catch { }

        MessageBox.Show(errorMsg, "UI异常", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // 防止程序崩溃
    }
}

