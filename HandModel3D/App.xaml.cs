using System.Windows;

namespace HandModel3D
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 개별 예외 처리가 놓친 예외로 앱 전체가 죽어 작업(포즈)이 날아가지 않게 하는
            // 마지막 안전망(배포 exe 전제, OTP 와 동일한 관례).
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show("예상하지 못한 오류가 발생했습니다.\n" + args.Exception.Message,
                                "손 모델 3D",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
