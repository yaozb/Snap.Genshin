﻿using DGP.Genshin.Control.Launching;
using DGP.Genshin.Core.Notification;
using DGP.Genshin.DataModel.Launching;
using DGP.Genshin.Factory.Abstraction;
using DGP.Genshin.Message;
using DGP.Genshin.Page;
using DGP.Genshin.Service.Abstraction.Launching;
using DGP.Genshin.Service.Abstraction.Setting;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Mvvm.Messaging;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.VisualStudio.Threading;
using ModernWpf.Controls;
using Snap.Core.DependencyInjection;
using Snap.Core.Mvvm;
using Snap.Data.Primitive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DGP.Genshin.ViewModel
{
    [ViewModel(InjectAs.Transient)]
    internal class LaunchViewModel : ObservableObject2
    {
        private readonly ILaunchService launchService;
        private readonly IMessenger messenger;

        #region Observables
        private List<LaunchScheme> knownSchemes = new()
        {
            new LaunchScheme(name: "官方服 | 天空岛", channel: "1", cps: "pcadbdpz", subChannel: "1"),
            new LaunchScheme(name: "渠道服 | 世界树", channel: "14", cps: "bilibili", subChannel: "0"),
            new LaunchScheme(name: "国际服 | 插件支持", channel: "1", cps: "mihoyo", subChannel: "0")
        };

        private LaunchScheme? currentScheme;
        private bool isBorderless;
        private bool isFullScreen;
        private ObservableCollection<GenshinAccount> accounts = new();
        private GenshinAccount? selectedAccount;
        private bool unlockFPS;
        private double targetFPS;
        private bool? isElevated;
        private long screenWidth;
        private long screenHeight;

        /// <summary>
        /// 已知的启动方案
        /// </summary>
        public List<LaunchScheme> KnownSchemes
        {
            get => knownSchemes;

            set => SetProperty(ref knownSchemes, value);
        }
        /// <summary>
        /// 当前启动方案
        /// </summary>
        public LaunchScheme? CurrentScheme
        {
            get => currentScheme;

            set => SetPropertyAndCallbackOnCompletion(ref currentScheme, value, v => launchService.SaveLaunchScheme(v));
        }
        /// <summary>
        /// 是否全屏
        /// </summary>
        public bool IsFullScreen
        {
            get => isFullScreen;

            set => SetPropertyAndCallbackOnCompletion(ref isFullScreen, value, Setting2.IsFullScreen.Set);
        }
        /// <summary>
        /// 是否无边框窗口
        /// </summary>
        public bool IsBorderless
        {
            get => isBorderless;

            set => SetPropertyAndCallbackOnCompletion(ref isBorderless, value, Setting2.IsBorderless.Set);
        }
        /// <summary>
        /// 是否解锁FPS上限
        /// </summary>
        public bool UnlockFPS
        {
            get => unlockFPS;

            set => SetPropertyAndCallbackOnCompletion(ref unlockFPS, value, Setting2.UnlockFPS.Set);
        }
        /// <summary>
        /// 目标帧率
        /// </summary>
        public double TargetFPS
        {
            get => targetFPS;

            set => SetPropertyAndCallbackOnCompletion(ref targetFPS, value, OnTargetFPSChanged);
        }
        [PropertyChangedCallback]
        private void OnTargetFPSChanged(double value)
        {
            Setting2.TargetFPS.Set(value);
            launchService.SetTargetFPSDynamically((int)value);
        }
        public long ScreenWidth
        {
            get => screenWidth;

            set => SetPropertyAndCallbackOnCompletion(ref screenWidth, value, Setting2.ScreenWidth.Set);
        }
        public long ScreenHeight
        {
            get => screenHeight;

            set => SetPropertyAndCallbackOnCompletion(ref screenHeight, value, Setting2.ScreenHeight.Set);
        }
        public bool IsElevated
        {
            get
            {
                isElevated ??= App.IsElevated;
                return isElevated.Value;
            }
        }
        public ObservableCollection<GenshinAccount> Accounts
        {
            get => accounts;

            set => SetProperty(ref accounts, value);
        }
        public GenshinAccount? SelectedAccount
        {
            get => selectedAccount;

            set => SetPropertyAndCallbackOnCompletion(ref selectedAccount, value, v => launchService.SetToRegistry(v));
        }

        public WorkWatcher GameWatcher { get; }

        public ICommand OpenUICommand { get; }
        public ICommand LaunchCommand { get; }
        public ICommand MatchAccountCommand { get; }
        public ICommand DeleteAccountCommand { get; }
        public ICommand ReselectLauncherPathCommand { get; }
        #endregion

        public LaunchViewModel(IMessenger messenger, IAsyncRelayCommandFactory asyncRelayCommandFactory)
        {
            launchService = App.Current.SwitchableImplementationManager.CurrentLaunchService!.Factory.Value;
            GameWatcher = launchService.GameWatcher;

            this.messenger = messenger;

            Accounts = launchService.LoadAllAccount();

            IsBorderless = Setting2.IsBorderless;
            IsFullScreen = Setting2.IsFullScreen;
            UnlockFPS = Setting2.UnlockFPS;
            TargetFPS = Setting2.TargetFPS;
            ScreenWidth = Setting2.ScreenWidth;
            ScreenHeight = Setting2.ScreenHeight;

            OpenUICommand = asyncRelayCommandFactory.Create(OpenUIAsync);
            LaunchCommand = asyncRelayCommandFactory.Create<string>(LaunchByOptionAsync);
            MatchAccountCommand = asyncRelayCommandFactory.Create(() => MatchAccountAsync(true));
            DeleteAccountCommand = new RelayCommand(DeleteAccount);
            ReselectLauncherPathCommand = asyncRelayCommandFactory.Create(ReselectLauncherPathAsync);
        }

        private async Task OpenUIAsync()
        {
            string? launcherPath = Setting2.LauncherPath;
            launcherPath = launchService.SelectLaunchDirectoryIfIncorrect(launcherPath);
            if (launcherPath is not null && launchService.TryLoadIniData(launcherPath))
            {
                await MatchAccountAsync();
                CurrentScheme = KnownSchemes
                    .First(item => item.Channel == launchService.GameConfig["General"]["channel"]);
            }
            else
            {
                Setting2.LauncherPath.Set(null);
                await App.Current.Dispatcher.InvokeAsync(new ContentDialog()
                {
                    Title = "请尝试重新选择启动器路径",
                    Content = "可能是启动器路径设置错误\n或者读取游戏配置文件失败",
                    PrimaryButtonText = "确定"
                }.ShowAsync).Task.Unwrap();
                messenger.Send(new NavigateRequestMessage(typeof(HomePage), true));
            }
        }
        private async Task LaunchByOptionAsync(string? option)
        {
            switch (option)
            {
                case "Launcher":
                    {
                        launchService.OpenOfficialLauncher(ex =>
                        HandleLaunchFailureAsync("打开启动器失败", ex).Forget());
                        break;
                    }
                case "Game":
                    {
                        await launchService.LaunchAsync(LaunchOption.FromCurrentSettings(), ex =>
                        HandleLaunchFailureAsync("启动游戏失败", ex).Forget());
                        break;
                    }
            }
        }
        private void SaveAllAccounts()
        {
            launchService.SaveAllAccounts(Accounts);
        }
        private void DeleteAccount()
        {
            if (SelectedAccount is not null)
            {
                Accounts.Remove(SelectedAccount);
                SelectedAccount = Accounts.LastOrDefault();
                SaveAllAccounts();
            }
        }

        /// <summary>
        /// 从注册表获取当前的账户信息
        /// </summary>
        private async Task MatchAccountAsync(bool allowNewAccount = false)
        {
            //注册表内有账号信息
            if (launchService.GetFromRegistry() is GenshinAccount currentRegistryAccount)
            {
                GenshinAccount? matched = Accounts.FirstOrDefault(a => a.MihoyoSDK == currentRegistryAccount.MihoyoSDK);
                //账号列表内存在匹配项
                if (matched is not null)
                {
                    selectedAccount = matched;
                }
                else
                {
                    if (allowNewAccount)
                    {
                        //命名
                        currentRegistryAccount.Name = await new NameDialog { TargetAccount = currentRegistryAccount }.GetInputAsync();
                        Accounts.Add(currentRegistryAccount);
                        selectedAccount = currentRegistryAccount;
                    }
                }
                //prevent registry set
                OnPropertyChanged(nameof(SelectedAccount));
                SaveAllAccounts();
            }
            else
            {
                SelectedAccount = Accounts.FirstOrDefault();
                new ToastContentBuilder()
                .AddText("从注册表获取账号信息失败")
                .SafeShow();
            }
        }
        private async Task ReselectLauncherPathAsync()
        {
            Setting2.LauncherPath.Set(null);
            string? launcherPath = Setting2.LauncherPath;
            launcherPath = launchService.SelectLaunchDirectoryIfIncorrect(launcherPath);
            if (launcherPath is not null && launchService.TryLoadIniData(launcherPath))
            {
                await MatchAccountAsync();
                CurrentScheme = KnownSchemes
                    .First(item => item.Channel == launchService.GameConfig["General"]["channel"]);
            }
            else
            {
                Setting2.LauncherPath.Set(null);
                await App.Current.Dispatcher.InvokeAsync(new ContentDialog()
                {
                    Title = "请尝试重新选择启动器路径",
                    Content = "可能是启动器路径设置错误\n或者读取游戏配置文件失败",
                    PrimaryButtonText = "确定"
                }.ShowAsync).Task.Unwrap();
                messenger.Send(new NavigateRequestMessage(typeof(HomePage), true));
            }
        }
        private async Task HandleLaunchFailureAsync(string title, Exception exception)
        {
            await new ContentDialog()
            {
                Title = title,
                Content = exception.Message,
                PrimaryButtonText = "确定",
                DefaultButton = ContentDialogButton.Primary
            }.ShowAsync();
        }
    }
}
