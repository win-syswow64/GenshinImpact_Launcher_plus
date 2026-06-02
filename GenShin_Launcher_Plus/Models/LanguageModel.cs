using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GenShin_Launcher_Plus.Models
{
    public class LanguageModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        // Raise PropertyChanged for all properties (called after language switch)
        public void RaiseAllChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        // === Language metadata ===
        private string _langVersion;
        public string LangVersion { get => _langVersion; set => SetField(ref _langVersion, value); }

        private string _languages;
        public string Languages { get => _languages; set => SetField(ref _languages, value); }

        // === Common buttons / labels ===
        private string _saveBtn;
        public string SaveBtn { get => _saveBtn; set => SetField(ref _saveBtn, value); }

        private string _cancel;
        public string Cancel { get => _cancel; set => SetField(ref _cancel, value); }

        private string _tipsStr;
        public string TipsStr { get => _tipsStr; set => SetField(ref _tipsStr, value); }

        private string _error;
        public string Error { get => _error; set => SetField(ref _error, value); }

        private string _determine;
        public string Determine { get => _determine; set => SetField(ref _determine, value); }

        private string _languageSetTitle;
        public string LanguageSetTitle { get => _languageSetTitle; set => SetField(ref _languageSetTitle, value); }

        // === Main page ===
        private string _mainTitle;
        public string MainTitle { get => _mainTitle; set => SetField(ref _mainTitle, value); }

        private string _screenPathErr;
        public string ScreenPathErr { get => _screenPathErr; set => SetField(ref _screenPathErr, value); }

        private string _aboutStr;
        public string AboutStr { get => _aboutStr; set => SetField(ref _aboutStr, value); }

        private string _aboutTitle;
        public string AboutTitle { get => _aboutTitle; set => SetField(ref _aboutTitle, value); }

        // === Game launch ===
        private string _runGameBtn;
        public string RunGameBtn { get => _runGameBtn; set => SetField(ref _runGameBtn, value); }

        private string _userNameLab;
        public string UserNameLab { get => _userNameLab; set => SetField(ref _userNameLab, value); }

        private string _gamePortLab;
        public string GamePortLab { get => _gamePortLab; set => SetField(ref _gamePortLab, value); }

        private string _userNameStr;
        public string UserNameStr { get => _userNameStr; set => SetField(ref _userNameStr, value); }

        private string _gameClientStr;
        public string GameClientStr { get => _gameClientStr; set => SetField(ref _gameClientStr, value); }

        private string _gameClientTypePStr;
        public string GameClientTypePStr { get => _gameClientTypePStr; set => SetField(ref _gameClientTypePStr, value); }

        private string _gameClientTypeBStr;
        public string GameClientTypeBStr { get => _gameClientTypeBStr; set => SetField(ref _gameClientTypeBStr, value); }

        private string _gameClientTypeMStr;
        public string GameClientTypeMStr { get => _gameClientTypeMStr; set => SetField(ref _gameClientTypeMStr, value); }

        private string _gameClientTypeNullStr;
        public string GameClientTypeNullStr { get => _gameClientTypeNullStr; set => SetField(ref _gameClientTypeNullStr, value); }

        private string _pathErrorMessageStr;
        public string PathErrorMessageStr { get => _pathErrorMessageStr; set => SetField(ref _pathErrorMessageStr, value); }

        private string _autoSearchToolTip;
        public string AutoSearchToolTip { get => _autoSearchToolTip; set => SetField(ref _autoSearchToolTip, value); }

        private string _gameFoundMsg;
        public string GameFoundMsg { get => _gameFoundMsg; set => SetField(ref _gameFoundMsg, value); }

        private string _gameNotFoundMsg;
        public string GameNotFoundMsg { get => _gameNotFoundMsg; set => SetField(ref _gameNotFoundMsg, value); }

        // === Account management ===
        private string _addUsersPageTitle;
        public string AddUsersPageTitle { get => _addUsersPageTitle; set => SetField(ref _addUsersPageTitle, value); }

        private string _addUsersPageSubTitle;
        public string AddUsersPageSubTitle { get => _addUsersPageSubTitle; set => SetField(ref _addUsersPageSubTitle, value); }

        private string _addUsersPageTextboxTips;
        public string AddUsersPageTextboxTips { get => _addUsersPageTextboxTips; set => SetField(ref _addUsersPageTextboxTips, value); }

        private string _addUsersErrorMessageStr;
        public string AddUsersErrorMessageStr { get => _addUsersErrorMessageStr; set => SetField(ref _addUsersErrorMessageStr, value); }

        private string _saveAccountErr;
        public string SaveAccountErr { get => _saveAccountErr; set => SetField(ref _saveAccountErr, value); }

        // === Settings ===
        private string _settingsTitle;
        public string SettingsTitle { get => _settingsTitle; set => SetField(ref _settingsTitle, value); }

        private string _closeBtn;
        public string CloseBtn { get => _closeBtn; set => SetField(ref _closeBtn, value); }

        private string _heightStr;
        public string HeightStr { get => _heightStr; set => SetField(ref _heightStr, value); }

        private string _witdhStr;
        public string WitdhStr { get => _witdhStr; set => SetField(ref _witdhStr, value); }

        private string _attachTitle;
        public string AttachTitle { get => _attachTitle; set => SetField(ref _attachTitle, value); }

        private string _userListTitle;
        public string UserListTitle { get => _userListTitle; set => SetField(ref _userListTitle, value); }

        private string _clientSwitchTitle;
        public string ClientSwitchTitle { get => _clientSwitchTitle; set => SetField(ref _clientSwitchTitle, value); }

        private string _pathBoxTips;
        public string PathBoxTips { get => _pathBoxTips; set => SetField(ref _pathBoxTips, value); }

        private string _delUserBtn;
        public string DelUserBtn { get => _delUserBtn; set => SetField(ref _delUserBtn, value); }

        private string _switchBtn;
        public string SwitchBtn { get => _switchBtn; set => SetField(ref _switchBtn, value); }

        private string _borderlessCkB;
        public string BorderlessCkB { get => _borderlessCkB; set => SetField(ref _borderlessCkB, value); }

        private string _unlockFpsCkB;
        public string UnlockFpsCkB { get => _unlockFpsCkB; set => SetField(ref _unlockFpsCkB, value); }

        private string _fpsBoxTips;
        public string FpsBoxTips { get => _fpsBoxTips; set => SetField(ref _fpsBoxTips, value); }

        private string _backgroundCkB;
        public string BackgroundCkB { get => _backgroundCkB; set => SetField(ref _backgroundCkB, value); }

        private string _backgroundXK;
        public string BackgroundXK { get => _backgroundXK; set => SetField(ref _backgroundXK, value); }

        private string _gameSettingsBtn;
        public string GameSettingsBtn { get => _gameSettingsBtn; set => SetField(ref _gameSettingsBtn, value); }

        private string _gameClientSwitchBtn;
        public string GameClientSwitchBtn { get => _gameClientSwitchBtn; set => SetField(ref _gameClientSwitchBtn, value); }

        private string _accountSettingsBtn;
        public string AccountSettingsBtn { get => _accountSettingsBtn; set => SetField(ref _accountSettingsBtn, value); }

        private string _programSettingsBtn;
        public string ProgramSettingsBtn { get => _programSettingsBtn; set => SetField(ref _programSettingsBtn, value); }

        private string _setDisplaySizeLab;
        public string SetDisplaySizeLab { get => _setDisplaySizeLab; set => SetField(ref _setDisplaySizeLab, value); }

        private string _chooseDisplaySizeListLab;
        public string ChooseDisplaySizeListLab { get => _chooseDisplaySizeListLab; set => SetField(ref _chooseDisplaySizeListLab, value); }

        private string _setGameClientLab;
        public string SetGameClientLab { get => _setGameClientLab; set => SetField(ref _setGameClientLab, value); }

        private string _setGameWindowModeLab;
        public string SetGameWindowModeLab { get => _setGameWindowModeLab; set => SetField(ref _setGameWindowModeLab, value); }

        private string _setBorderlessLab;
        public string SetBorderlessLab { get => _setBorderlessLab; set => SetField(ref _setBorderlessLab, value); }

        private string _setUnLockFpsLab;
        public string SetUnLockFpsLab { get => _setUnLockFpsLab; set => SetField(ref _setUnLockFpsLab, value); }

        private string _startedCloseCkb;
        public string StartedCloseCkb { get => _startedCloseCkb; set => SetField(ref _startedCloseCkb, value); }

        private string _noCheckUpdateCkb;
        public string NoCheckUpdateCkb { get => _noCheckUpdateCkb; set => SetField(ref _noCheckUpdateCkb, value); }

        private string _checkUpdateBtn;
        public string CheckUpdateBtn { get => _checkUpdateBtn; set => SetField(ref _checkUpdateBtn, value); }

        private string _openProgameFolderBtn;
        public string OpenProgameFolderBtn { get => _openProgameFolderBtn; set => SetField(ref _openProgameFolderBtn, value); }

        private string _progameLanguageSetBtn;
        public string ProgameLanguageSetBtn { get => _progameLanguageSetBtn; set => SetField(ref _progameLanguageSetBtn, value); }

        private string _setMainBackgroundBtn;
        public string SetMainBackgroundBtn { get => _setMainBackgroundBtn; set => SetField(ref _setMainBackgroundBtn, value); }

        // === Client conversion ===
        private string _stateIndicatorDefault;
        public string StateIndicatorDefault { get => _stateIndicatorDefault; set => SetField(ref _stateIndicatorDefault, value); }

        private string _stateIndicatorUning;
        public string StateIndicatorUning { get => _stateIndicatorUning; set => SetField(ref _stateIndicatorUning, value); }

        private string _stateIndicatorUpdate;
        public string StateIndicatorUpdate { get => _stateIndicatorUpdate; set => SetField(ref _stateIndicatorUpdate, value); }

        private string _stateIndicatorCheck;
        public string StateIndicatorCheck { get => _stateIndicatorCheck; set => SetField(ref _stateIndicatorCheck, value); }

        private string _stateIndicatorUnErr;
        public string StateIndicatorUnErr { get => _stateIndicatorUnErr; set => SetField(ref _stateIndicatorUnErr, value); }

        private string _stateIndicatorBaking;
        public string StateIndicatorBaking { get => _stateIndicatorBaking; set => SetField(ref _stateIndicatorBaking, value); }

        private string _stateIndicatorReping;
        public string StateIndicatorReping { get => _stateIndicatorReping; set => SetField(ref _stateIndicatorReping, value); }

        private string _stateIndicatorRecover;
        public string StateIndicatorRecover { get => _stateIndicatorRecover; set => SetField(ref _stateIndicatorRecover, value); }

        private string _stateIndicatorCleaning;
        public string StateIndicatorCleaning { get => _stateIndicatorCleaning; set => SetField(ref _stateIndicatorCleaning, value); }

        private string _convertingLogStr;
        public string ConvertingLogStr { get => _convertingLogStr; set => SetField(ref _convertingLogStr, value); }

        private string _windowMode;
        public string WindowMode { get => _windowMode; set => SetField(ref _windowMode, value); }

        private string _fullscreen;
        public string Fullscreen { get => _fullscreen; set => SetField(ref _fullscreen, value); }

        private string _gameDirMsg;
        public string GameDirMsg { get => _gameDirMsg; set => SetField(ref _gameDirMsg, value); }

        private string _adaptiveStr;
        public string AdaptiveStr { get => _adaptiveStr; set => SetField(ref _adaptiveStr, value); }

        // === Warnings / Errors ===
        private string _severeWarning;
        public string SevereWarning { get => _severeWarning; set => SetField(ref _severeWarning, value); }

        private string _severeWarningStr;
        public string SevereWarningStr { get => _severeWarningStr; set => SetField(ref _severeWarningStr, value); }

        private string _warning;
        public string Warning { get => _warning; set => SetField(ref _warning, value); }

        private string _warningDAW;
        public string WarningDAW { get => _warningDAW; set => SetField(ref _warningDAW, value); }

        private string _errorSA;
        public string ErrorSA { get => _errorSA; set => SetField(ref _errorSA, value); }

        private string _errorEYJ;
        public string ErrorEYJ { get => _errorEYJ; set => SetField(ref _errorEYJ, value); }

        private string _warningCCStr;
        public string WarningCCStr { get => _warningCCStr; set => SetField(ref _warningCCStr, value); }

        private string _errorPkgUnzip;
        public string ErrorPkgUnzip { get => _errorPkgUnzip; set => SetField(ref _errorPkgUnzip, value); }

        private string _errorPkgNF;
        public string ErrorPkgNF { get => _errorPkgNF; set => SetField(ref _errorPkgNF, value); }

        private string _pkgNoUnError;
        public string PkgNoUnError { get => _pkgNoUnError; set => SetField(ref _pkgNoUnError, value); }

        private string _newPkgVer;
        public string NewPkgVer { get => _newPkgVer; set => SetField(ref _newPkgVer, value); }

        private string _errorFileNF;
        public string ErrorFileNF { get => _errorFileNF; set => SetField(ref _errorFileNF, value); }

        private string _fileExist;
        public string FileExist { get => _fileExist; set => SetField(ref _fileExist, value); }

        private string _errorBakF;
        public string ErrorBakF { get => _errorBakF; set => SetField(ref _errorBakF, value); }

        private string _bakSuccess;
        public string BakSuccess { get => _bakSuccess; set => SetField(ref _bakSuccess, value); }

        private string _bakFileNfSk;
        public string BakFileNfSk { get => _bakFileNfSk; set => SetField(ref _bakFileNfSk, value); }

        private string _repSuccess;
        public string RepSuccess { get => _repSuccess; set => SetField(ref _repSuccess, value); }

        private string _switchSucessStr;
        public string SwitchSucessStr { get => _switchSucessStr; set => SetField(ref _switchSucessStr, value); }

        private string _cleanedStr;
        public string CleanedStr { get => _cleanedStr; set => SetField(ref _cleanedStr, value); }

        private string _cleanSkipStr;
        public string CleanSkipStr { get => _cleanSkipStr; set => SetField(ref _cleanSkipStr, value); }

        private string _restoreSucess;
        public string RestoreSucess { get => _restoreSucess; set => SetField(ref _restoreSucess, value); }

        private string _restoreSkipStr;
        public string RestoreSkipStr { get => _restoreSkipStr; set => SetField(ref _restoreSkipStr, value); }

        private string _restoreOverTipsStr;
        public string RestoreOverTipsStr { get => _restoreOverTipsStr; set => SetField(ref _restoreOverTipsStr, value); }

        private string _restoreNum;
        public string RestoreNum { get => _restoreNum; set => SetField(ref _restoreNum, value); }

        private string _restoreErrNum;
        public string RestoreErrNum { get => _restoreErrNum; set => SetField(ref _restoreErrNum, value); }

        private string _restoreEndStr;
        public string RestoreEndStr { get => _restoreEndStr; set => SetField(ref _restoreEndStr, value); }

        private string _closeGameWaring;
        public string CloseGameWaring { get => _closeGameWaring; set => SetField(ref _closeGameWaring, value); }

        private string _convertError;
        public string ConvertError { get => _convertError; set => SetField(ref _convertError, value); }

        private string _getPkgFileBtn;
        public string GetPkgFileBtn { get => _getPkgFileBtn; set => SetField(ref _getPkgFileBtn, value); }

        private string _prePkgFileBtn;
        public string PrePkgFileBtn { get => _prePkgFileBtn; set => SetField(ref _prePkgFileBtn, value); }

        // === Update page ===
        private string _downPageTips;
        public string DownPageTips { get => _downPageTips; set => SetField(ref _downPageTips, value); }

        private string _updateSkipBtn;
        public string UpdateSkipBtn { get => _updateSkipBtn; set => SetField(ref _updateSkipBtn, value); }

        private string _downStartBtn;
        public string DownStartBtn { get => _downStartBtn; set => SetField(ref _downStartBtn, value); }

        private string _useGlobalUrl;
        public string UseGlobalUrl { get => _useGlobalUrl; set => SetField(ref _useGlobalUrl, value); }

        private string _downFailedStr;
        public string DownFailedStr { get => _downFailedStr; set => SetField(ref _downFailedStr, value); }

        private string _repWarnStr;
        public string RepWarnStr { get => _repWarnStr; set => SetField(ref _repWarnStr, value); }

        private string _downloadComStr;
        public string DownloadComStr { get => _downloadComStr; set => SetField(ref _downloadComStr, value); }

        private string _downProgress;
        public string DownProgress { get => _downProgress; set => SetField(ref _downProgress, value); }

        // === Guide page ===
        private string _welcomeTitle;
        public string WelcomeTitle { get => _welcomeTitle; set => SetField(ref _welcomeTitle, value); }

        private string _bootstrapTitle;
        public string BootstrapTitle { get => _bootstrapTitle; set => SetField(ref _bootstrapTitle, value); }

        private string _pathHintLabel;
        public string PathHintLabel { get => _pathHintLabel; set => SetField(ref _pathHintLabel, value); }

        private string _finalTipLabel;
        public string FinalTipLabel { get => _finalTipLabel; set => SetField(ref _finalTipLabel, value); }

        private string _guideFinishBtn;
        public string GuideFinishBtn { get => _guideFinishBtn; set => SetField(ref _guideFinishBtn, value); }        // === Sidebar navigation ===
        private string _navScreenshotsText;
        public string NavScreenshotsText { get => _navScreenshotsText; set => SetField(ref _navScreenshotsText, value); }
        private string _navScreenshotsToolTip;
        public string NavScreenshotsToolTip { get => _navScreenshotsToolTip; set => SetField(ref _navScreenshotsToolTip, value); }
        private string _navQQGroupText;
        public string NavQQGroupText { get => _navQQGroupText; set => SetField(ref _navQQGroupText, value); }
        private string _navAboutText;
        public string NavAboutText { get => _navAboutText; set => SetField(ref _navAboutText, value); }
        private string _navSettingsText;
        public string NavSettingsText { get => _navSettingsText; set => SetField(ref _navSettingsText, value); }
        private string _minimizeToolTip;
        public string MinimizeToolTip { get => _minimizeToolTip; set => SetField(ref _minimizeToolTip, value); }
        private string _closeToolTip;
        public string CloseToolTip { get => _closeToolTip; set => SetField(ref _closeToolTip, value); }
        // === Home page ===
        private string _accountLabel;
        public string AccountLabel { get => _accountLabel; set => SetField(ref _accountLabel, value); }
        private string _clientLabel;
        public string ClientLabel { get => _clientLabel; set => SetField(ref _clientLabel, value); }
        // === Setting page tabs ===
        private string _backToolTip;
        public string BackToolTip { get => _backToolTip; set => SetField(ref _backToolTip, value); }
        private string _gameSettingsTab;
        public string GameSettingsTab { get => _gameSettingsTab; set => SetField(ref _gameSettingsTab, value); }
        private string _clientConvertTab;
        public string ClientConvertTab { get => _clientConvertTab; set => SetField(ref _clientConvertTab, value); }

        private string _convertTargetHeader;
        public string ConvertTargetHeader { get => _convertTargetHeader; set => SetField(ref _convertTargetHeader, value); }
        private string _accountManageTab;
        public string AccountManageTab { get => _accountManageTab; set => SetField(ref _accountManageTab, value); }
        private string _themeSettingsTab;
        public string ThemeSettingsTab { get => _themeSettingsTab; set => SetField(ref _themeSettingsTab, value); }
        private string _languageSettingsTab;
        public string LanguageSettingsTab { get => _languageSettingsTab; set => SetField(ref _languageSettingsTab, value); }
        private string _functionSettingsTab;
        public string FunctionSettingsTab { get => _functionSettingsTab; set => SetField(ref _functionSettingsTab, value); }
        private string _programSettingsTab;
        public string ProgramSettingsTab { get => _programSettingsTab; set => SetField(ref _programSettingsTab, value); }
        // === Setting page - Game settings ===
        private string _customResolutionHeader;
        public string CustomResolutionHeader { get => _customResolutionHeader; set => SetField(ref _customResolutionHeader, value); }
        private string _addPresetToolTip;
        public string AddPresetToolTip { get => _addPresetToolTip; set => SetField(ref _addPresetToolTip, value); }
        private string _presetResolutionHeader;
        public string PresetResolutionHeader { get => _presetResolutionHeader; set => SetField(ref _presetResolutionHeader, value); }
        private string _gameServerHeader;
        public string GameServerHeader { get => _gameServerHeader; set => SetField(ref _gameServerHeader, value); }
        private string _windowModeHeader;
        public string WindowModeHeader { get => _windowModeHeader; set => SetField(ref _windowModeHeader, value); }
        private string _gameDirectoryHeader;
        public string GameDirectoryHeader { get => _gameDirectoryHeader; set => SetField(ref _gameDirectoryHeader, value); }
        // === Setting page - Account ===
        private string _accountListHeader;
        public string AccountListHeader { get => _accountListHeader; set => SetField(ref _accountListHeader, value); }
        // === Setting page - Theme ===
        private string _themeColorHeader;
        public string ThemeColorHeader { get => _themeColorHeader; set => SetField(ref _themeColorHeader, value); }
        private string _backgroundSettingsHeader;
        public string BackgroundSettingsHeader { get => _backgroundSettingsHeader; set => SetField(ref _backgroundSettingsHeader, value); }
        private string _customBackgroundHeader;
        public string CustomBackgroundHeader { get => _customBackgroundHeader; set => SetField(ref _customBackgroundHeader, value); }
        private string _chooseImageBtn;
        public string ChooseImageBtn { get => _chooseImageBtn; set => SetField(ref _chooseImageBtn, value); }
        private string _saveTodayBgBtn;
        public string SaveTodayBgBtn { get => _saveTodayBgBtn; set => SetField(ref _saveTodayBgBtn, value); }
        // === Setting page - Language ===
        private string _saveAndRestartBtn;
        public string SaveAndRestartBtn { get => _saveAndRestartBtn; set => SetField(ref _saveAndRestartBtn, value); }
        // === Setting page - Function ===
        private string _navBarFunctionsHeader;
        public string NavBarFunctionsHeader { get => _navBarFunctionsHeader; set => SetField(ref _navBarFunctionsHeader, value); }
        private string _navBarFunctionsDesc;
        public string NavBarFunctionsDesc { get => _navBarFunctionsDesc; set => SetField(ref _navBarFunctionsDesc, value); }
        private string _navScreenshotsCkb;
        public string NavScreenshotsCkb { get => _navScreenshotsCkb; set => SetField(ref _navScreenshotsCkb, value); }
        private string _navQQGroupCkb;
        public string NavQQGroupCkb { get => _navQQGroupCkb; set => SetField(ref _navQQGroupCkb, value); }
        private string _navAboutCkb;
        public string NavAboutCkb { get => _navAboutCkb; set => SetField(ref _navAboutCkb, value); }
        // === Setting page - Program ===
        private string _programSettingsHeader;
        public string ProgramSettingsHeader { get => _programSettingsHeader; set => SetField(ref _programSettingsHeader, value); }
        private string _quickActionsHeader;
        public string QuickActionsHeader { get => _quickActionsHeader; set => SetField(ref _quickActionsHeader, value); }
        private string _recoverDefaultSizeBtn;
        public string RecoverDefaultSizeBtn { get => _recoverDefaultSizeBtn; set => SetField(ref _recoverDefaultSizeBtn, value); }
        // === Users page ===
        private string _saveGameConfigCkb;
        public string SaveGameConfigCkb { get => _saveGameConfigCkb; set => SetField(ref _saveGameConfigCkb, value); }
        // === Dialog messages ===
        private string _exitConfirmMessage;
        public string ExitConfirmMessage { get => _exitConfirmMessage; set => SetField(ref _exitConfirmMessage, value); }
        private string _addResolutionSuccess;
        public string AddResolutionSuccess { get => _addResolutionSuccess; set => SetField(ref _addResolutionSuccess, value); }
        private string _resolutionInputError;
        public string ResolutionInputError { get => _resolutionInputError; set => SetField(ref _resolutionInputError, value); }
        private string _removeResolutionSuccess;
        public string RemoveResolutionSuccess { get => _removeResolutionSuccess; set => SetField(ref _removeResolutionSuccess, value); }
        private string _noPresetSelected;
        public string NoPresetSelected { get => _noPresetSelected; set => SetField(ref _noPresetSelected, value); }
        private string _saveImageDialogTitle;
        public string SaveImageDialogTitle { get => _saveImageDialogTitle; set => SetField(ref _saveImageDialogTitle, value); }
        private string _saveImageSuccess;
        public string SaveImageSuccess { get => _saveImageSuccess; set => SetField(ref _saveImageSuccess, value); }
        private string _noCachedBackground;
        public string NoCachedBackground { get => _noCachedBackground; set => SetField(ref _noCachedBackground, value); }
        private string _chooseBgDialogTitle;
        public string ChooseBgDialogTitle { get => _chooseBgDialogTitle; set => SetField(ref _chooseBgDialogTitle, value); }
        private string _waitBgLoading;
        public string WaitBgLoading { get => _waitBgLoading; set => SetField(ref _waitBgLoading, value); }
        private string _noSavedPresets;
        public string NoSavedPresets { get => _noSavedPresets; set => SetField(ref _noSavedPresets, value); }
        private string _emptyAccountNameError;
        public string EmptyAccountNameError { get => _emptyAccountNameError; set => SetField(ref _emptyAccountNameError, value); }
        // === Game convert log/status ===
        private string _getPkgVersionFailed;
        public string GetPkgVersionFailed { get => _getPkgVersionFailed; set => SetField(ref _getPkgVersionFailed, value); }
        private string _getPkgVersionRetry;
        public string GetPkgVersionRetry { get => _getPkgVersionRetry; set => SetField(ref _getPkgVersionRetry, value); }
        private string _gameVersionTooLow;
        public string GameVersionTooLow { get => _gameVersionTooLow; set => SetField(ref _gameVersionTooLow, value); }

        private string _gameVersionInfo;
        public string GameVersionInfo { get => _gameVersionInfo; set => SetField(ref _gameVersionInfo, value); }
        private string _gettingBackupList;
        public string GettingBackupList { get => _gettingBackupList; set => SetField(ref _gettingBackupList, value); }
        private string _restoringClient;
        public string RestoringClient { get => _restoringClient; set => SetField(ref _restoringClient, value); }
        private string _pkgCopyLost;
        public string PkgCopyLost { get => _pkgCopyLost; set => SetField(ref _pkgCopyLost, value); }
        private string _gettingFileList;
        public string GettingFileList { get => _gettingFileList; set => SetField(ref _gettingFileList, value); }
        private string _extractingPkg;
        public string ExtractingPkg { get => _extractingPkg; set => SetField(ref _extractingPkg, value); }
        private string _extractFailedLog;
        public string ExtractFailedLog { get => _extractFailedLog; set => SetField(ref _extractFailedLog, value); }
        private string _pkgFileNotExistLog;
        public string PkgFileNotExistLog { get => _pkgFileNotExistLog; set => SetField(ref _pkgFileNotExistLog, value); }
        private string _noStateStatus;
        public string NoStateStatus { get => _noStateStatus; set => SetField(ref _noStateStatus, value); }
        private string _startBackupLog;
        public string StartBackupLog { get => _startBackupLog; set => SetField(ref _startBackupLog, value); }
        private string _deployPkgLog;
        public string DeployPkgLog { get => _deployPkgLog; set => SetField(ref _deployPkgLog, value); }
        private string _startReplaceStatus;
        public string StartReplaceStatus { get => _startReplaceStatus; set => SetField(ref _startReplaceStatus, value); }
        private string _replaceSuccessLog;
        public string ReplaceSuccessLog { get => _replaceSuccessLog; set => SetField(ref _replaceSuccessLog, value); }
        private string _replaceFailedLog;
        public string ReplaceFailedLog { get => _replaceFailedLog; set => SetField(ref _replaceFailedLog, value); }
        private string _allReplacedLog;
        public string AllReplacedLog { get => _allReplacedLog; set => SetField(ref _allReplacedLog, value); }
        private string _startRestoreLog;
        public string StartRestoreLog { get => _startRestoreLog; set => SetField(ref _startRestoreLog, value); }
        private string _restoreSuccessLog;
        public string RestoreSuccessLog { get => _restoreSuccessLog; set => SetField(ref _restoreSuccessLog, value); }
        private string _restoreFailedLog;
        public string RestoreFailedLog { get => _restoreFailedLog; set => SetField(ref _restoreFailedLog, value); }
        private string _allRestoredLog;
        public string AllRestoredLog { get => _allRestoredLog; set => SetField(ref _allRestoredLog, value); }
        private string _backupSuccessLog;
        public string BackupSuccessLog { get => _backupSuccessLog; set => SetField(ref _backupSuccessLog, value); }
        private string _backupFailedLog;
        public string BackupFailedLog { get => _backupFailedLog; set => SetField(ref _backupFailedLog, value); }
        private string _pkgExtractFailed;
        public string PkgExtractFailed { get => _pkgExtractFailed; set => SetField(ref _pkgExtractFailed, value); }
        private string _warningPrefix;
        public string WarningPrefix { get => _warningPrefix; set => SetField(ref _warningPrefix, value); }
        private string _downloadLatestPkg;
        public string DownloadLatestPkg { get => _downloadLatestPkg; set => SetField(ref _downloadLatestPkg, value); }
        private string _movePkgToAppDir;
        public string MovePkgToAppDir { get => _movePkgToAppDir; set => SetField(ref _movePkgToAppDir, value); }
        private string _sourceDirLabel;
        public string SourceDirLabel { get => _sourceDirLabel; set => SetField(ref _sourceDirLabel, value); }
        private string _targetDirLabel;
        public string TargetDirLabel { get => _targetDirLabel; set => SetField(ref _targetDirLabel, value); }
        private string _gameExeLabel;
        public string GameExeLabel { get => _gameExeLabel; set => SetField(ref _gameExeLabel, value); }
        private string _origGameExePath;
        public string OrigGameExePath { get => _origGameExePath; set => SetField(ref _origGameExePath, value); }
        private string _newGameExePath;
        public string NewGameExePath { get => _newGameExePath; set => SetField(ref _newGameExePath, value); }
        private string _fileNotExist;
        public string FileNotExist { get => _fileNotExist; set => SetField(ref _fileNotExist, value); }
        private string _gameSelectorLabel;
        public string GameSelectorLabel { get => _gameSelectorLabel; set => SetField(ref _gameSelectorLabel, value); }
        private string _selectGameTitle;
        public string SelectGameTitle { get => _selectGameTitle; set => SetField(ref _selectGameTitle, value); }

        // === Game Install / Update ===
        private string _installGameBtn;
        public string InstallGameBtn { get => _installGameBtn; set => SetField(ref _installGameBtn, value); }
        private string _updateGameBtn;
        public string UpdateGameBtn { get => _updateGameBtn; set => SetField(ref _updateGameBtn, value); }
        private string _preDownloadBtn;
        public string PreDownloadBtn { get => _preDownloadBtn; set => SetField(ref _preDownloadBtn, value); }
        private string _downloadingText;
        public string DownloadingText { get => _downloadingText; set => SetField(ref _downloadingText, value); }
        private string _installingText;
        public string InstallingText { get => _installingText; set => SetField(ref _installingText, value); }
        private string _pauseBtn;
        public string PauseBtn { get => _pauseBtn; set => SetField(ref _pauseBtn, value); }
        private string _continueBtn;
        public string ContinueBtn { get => _continueBtn; set => SetField(ref _continueBtn, value); }
        private string _installCompleteText;
        public string InstallCompleteText { get => _installCompleteText; set => SetField(ref _installCompleteText, value); }
        private string _gameNotInstalledText;
        public string GameNotInstalledText { get => _gameNotInstalledText; set => SetField(ref _gameNotInstalledText, value); }
        private string _selectInstallPathText;
        public string SelectInstallPathText { get => _selectInstallPathText; set => SetField(ref _selectInstallPathText, value); }


    }
}

