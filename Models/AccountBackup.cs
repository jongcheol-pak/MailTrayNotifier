using MailTrayNotifier.ViewModels;

namespace MailTrayNotifier.Models
{
    /// <summary>
    /// 메일 계정 ViewModel의 백업 데이터를 저장하는 클래스 (Memento 패턴)
    /// </summary>
    public class AccountBackup
    {
        public string Pop3Server { get; set; } = string.Empty;
        public int Pop3Port { get; set; }
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public bool UseSsl { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int RefreshMinutes { get; set; }
        public string MailWebUrl { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string AccountName { get; set; } = string.Empty;

        /// <summary>
        /// ViewModel에서 백업 객체 생성
        /// </summary>
        public static AccountBackup CreateFrom(MailAccountViewModel viewModel)
        {
            return new AccountBackup
            {
                Pop3Server = viewModel.Pop3Server,
                Pop3Port = viewModel.Pop3Port,
                SmtpServer = viewModel.SmtpServer,
                SmtpPort = viewModel.SmtpPort,
                UseSsl = viewModel.UseSsl,
                UserId = viewModel.UserId,
                Password = viewModel.Password,
                RefreshMinutes = viewModel.RefreshMinutes,
                MailWebUrl = viewModel.MailWebUrl,
                IsEnabled = viewModel.IsEnabled,
                AccountName = viewModel.AccountName
            };
        }

        /// <summary>
        /// 백업 데이터로 ViewModel 복원
        /// </summary>
        public void RestoreTo(MailAccountViewModel viewModel)
        {
            viewModel.Pop3Server = Pop3Server;
            viewModel.Pop3Port = Pop3Port;
            viewModel.SmtpServer = SmtpServer;
            viewModel.SmtpPort = SmtpPort;
            viewModel.UseSsl = UseSsl;
            viewModel.UserId = UserId;
            viewModel.Password = Password;
            viewModel.RefreshMinutes = RefreshMinutes;
            viewModel.MailWebUrl = MailWebUrl;
            viewModel.IsEnabled = IsEnabled;
            viewModel.AccountName = AccountName;
        }
    }
}
