﻿using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Media;
using MahApps.Metro.Controls.Dialogs;
using System.Windows.Input;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Helper;
using System.Threading.Tasks;
using System.Windows.Data;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private IDialogCoordinator dialogCoordinator;
        public MainWindowViewModel(IDialogCoordinator instance, MainWindow main)
        {
            dialogCoordinator = instance;
            App.Current.LoadProgramCore.LoadLanguageCore();
            new UpdateService().CheckUpdate(main);
            MainService = new MainService(main, this);

            OpenAboutCommand = new RelayCommand(OpenAbout);
            OpenQQGroupUrlCommand = new RelayCommand(OpenQQGroupUrl);
            ExitProgramCommand = new RelayCommand(ExitProgram);
            MainMinimizedCommand = new RelayCommand(MainMinimized);
            OpenImagesDirectoryCommand = new RelayCommand(OpenImagesDirectory);

            Title = $"{languages.MainTitle} {Application.ResourceAssembly.GetName().Version}";
            App.Current.DataModel.EXEname(Path.GetFileName(Environment.ProcessPath));

            SetNotice();
           
        }

        public IMainWindowService MainService { get; set; }

        public LanguageModel languages { get => App.Current.Language; }
        public string Title { get; set; }
        private ImageBrush _Background;
        public ImageBrush Background { get => _Background; set => SetProperty(ref _Background, value); }

        public double MainWidth
        {
            get => App.Current.DataModel.MainWidth;
            set
            {
                App.Current.DataModel.MainWidth = value;
                App.Current.DataModel.SaveDataToFile();
            }
        }
        public double MainHeight
        {
            get => App.Current.DataModel.MainHeight;
            set
            {
                App.Current.DataModel.MainHeight = value;
                App.Current.DataModel.SaveDataToFile();
            }
        }

        public ICommand OpenImagesDirectoryCommand { get; set; }
        private async void OpenImagesDirectory()
        {
            if (Directory.Exists(Path.Combine(App.Current.DataModel.GamePath, "ScreenShot")))
            {
                FileHelper.OpenUrl(Path.Combine(App.Current.DataModel.GamePath, "ScreenShot"));
            }
            else
            {
                await dialogCoordinator.ShowMessageAsync(
                    this, languages.Error,
                    languages.ScreenPathErr,
                    MessageDialogStyle.Affirmative,
                    new MetroDialogSettings()
                    { AffirmativeButtonText = languages.Determine });
            }
        }

        private async Task SetNotice()
        {
            await MainService.CheckNotice();
            if (App.Current.NoticeObject.Code == 200)
            {
                await dialogCoordinator.ShowMessageAsync(
                   this, languages.TipsStr,
                   App.Current.NoticeObject.NoticeMsg,
                   MessageDialogStyle.Affirmative,
                   new MetroDialogSettings()
                   { AffirmativeButtonText = languages.Determine });
            }
        }

        public ICommand OpenAboutCommand { get; set; }
        private async void OpenAbout()
        {
            if ((await dialogCoordinator.ShowMessageAsync(
                this, languages.AboutTitle,
                languages.AboutStr,
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings()
                {
                    AffirmativeButtonText = languages.Determine,
                    NegativeButtonText = "GitHub"
                })) != MessageDialogResult.Affirmative)
            {
                FileHelper.OpenUrl("https://github.com/win-syswow64/GenshinImpact_Luncher_plus");
            }
        }

        public ICommand OpenQQGroupUrlCommand { get; set; }
        private void OpenQQGroupUrl()
        {
            FileHelper.OpenUrl("https://qm.qq.com/q/UZWuLb38om");
        }

        public ICommand ExitProgramCommand { get; set; }
        private void ExitProgram()
        {
            Environment.Exit(0);
        }

        public ICommand MainMinimizedCommand { get; set; }
        private void MainMinimized()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            });
        }
    }
}
