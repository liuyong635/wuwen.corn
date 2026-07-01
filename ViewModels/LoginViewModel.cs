using SeedCut.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 登录窗口的ViewModel
    /// </summary>
    public class LoginViewModel : INotifyPropertyChanged
    {
        #region 事件

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<UserType> LoginSuccess;

        #endregion

        #region 私有字段

        private UserType _selectedUserType = UserType.Admin; // 默认选中管理员
        private string _username = "admin"; // 默认账号
        private string _password = "admin123"; // 默认密码
        private string _errorMessage = string.Empty;
        private Visibility _errorVisibility = Visibility.Collapsed;

        #endregion

        #region 属性

        public UserType SelectedUserType
        {
            get => _selectedUserType;
            set
            {
                if (_selectedUserType != value)
                {
                    _selectedUserType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LoginTitle));
                }
            }
        }

        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged();
                    ClearError();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged();
                    ClearError();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                    ErrorVisibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        public Visibility ErrorVisibility
        {
            get => _errorVisibility;
            set
            {
                if (_errorVisibility != value)
                {
                    _errorVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LoginTitle
        {
            get
            {
                switch (SelectedUserType)
                {
                    case UserType.User:
                        return "用户登录";
                    case UserType.Admin:
                        return "管理员登录";
                    case UserType.Manufacturer:
                        return "厂家登录";
                    default:
                        return "登录";
                }
            }
        }

        #endregion

        #region 命令

        public ICommand SelectUserTypeCommand { get; }
        public ICommand LoginCommand { get; }

        #endregion

        #region 构造函数

        public LoginViewModel()
        {
            SelectUserTypeCommand = new RelayCommand<string>(OnSelectUserType);
            LoginCommand = new RelayCommand(OnLogin, CanLogin);
        }

        #endregion

        #region 命令处理

        private void OnSelectUserType(string userTypeStr)
        {
            if (Enum.TryParse<UserType>(userTypeStr, out var userType))
            {
                SelectedUserType = userType;
            }
        }

        private bool CanLogin()
        {
            return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        private void OnLogin()
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "请输入用户名";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "请输入密码";
                return;
            }

            // TODO: 实际的登录验证逻辑
            // 这里暂时使用简单的验证
            bool loginSuccess = ValidateLogin(Username, Password, SelectedUserType);

            if (loginSuccess)
            {
                LoginSuccess?.Invoke(this, SelectedUserType);
            }
            else
            {
                ErrorMessage = "用户名或密码错误";
            }
        }

        private bool ValidateLogin(string username, string password, UserType userType)
        {
            switch (userType)
            {
                case UserType.User:
                    return username == "user" && password == "123456";
                case UserType.Admin:
                    return username == "admin" && password == "admin123";
                case UserType.Manufacturer:
                    return username == "factory" && password == "factory123";
                default:
                    return false;
            }
        }

        private void ClearError()
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = string.Empty;
            }
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }


}