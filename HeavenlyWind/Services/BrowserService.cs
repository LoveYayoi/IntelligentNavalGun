﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sakuno.KanColle.Amatsukaze.Extensibility;
using Sakuno.KanColle.Amatsukaze.Extensibility.Services;
using Sakuno.KanColle.Amatsukaze.Services.Browser;
using Sakuno.UserInterface;
using Sakuno.UserInterface.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Sakuno.KanColle.Amatsukaze.Services
{
    class BrowserService : ModelBase, IBrowserService
    {
        public static BrowserService Instance { get; } = new BrowserService();

        internal MemoryMappedFileCommunicator Communicator { get; private set; }
        internal IConnectableObservable<KeyValuePair<string, string>> Messages { get; private set; }

        public IList<LayoutEngineInfo> InstalledLayoutEngines { get; private set; }

        public int HostProcessID => Process.GetCurrentProcess().Id;

        bool r_Initialized;

        bool r_NoInstalledLayoutEngines;
        public bool NoInstalledLayoutEngines
        {
            get { return r_NoInstalledLayoutEngines; }
            private set
            {
                if (r_NoInstalledLayoutEngines != value)
                {
                    r_NoInstalledLayoutEngines = value;
                    OnPropertyChanged(nameof(NoInstalledLayoutEngines));
                }
            }
        }

        Process r_BrowserProcess;
        public int? BrowserProcessID => r_BrowserProcess?.Id;

        public BrowserHost BrowserControl { get; private set; }
        public IntPtr Handle => BrowserControl != null ? BrowserControl.BrowserHandle : IntPtr.Zero;

        public BrowserNavigator Navigator { get; private set; }

        bool r_IsNavigatorVisible;
        public bool IsNavigatorVisible
        {
            get { return r_IsNavigatorVisible; }
            private set
            {
                if (r_IsNavigatorVisible != value)
                {
                    r_IsNavigatorVisible = value;
                    OnPropertyChanged(nameof(IsNavigatorVisible));
                }
            }
        }

        public bool IsResizedToFitGame { get; private set; }

        public GameController GameController { get; private set; }

        public ICommand ClearCacheCommand { get; }
        public ICommand ClearCacheAndCookieCommand { get; }

        public event Action Attached;
        public event EventHandler<Size> Resized;
        public event Action ResizedToFitGame;

        BrowserService()
        {
            r_IsNavigatorVisible = true;

            ClearCacheCommand = new DelegatedCommand(() => Communicator.Write(CommunicatorMessages.ClearCache));
            ClearCacheAndCookieCommand = new DelegatedCommand(() => Communicator.Write(CommunicatorMessages.ClearCacheAndCookie));
        }

        public void Initialize()
        {
            if (!r_Initialized)
            {
                if (!LoadLayoutEngines())
                {
                    NoInstalledLayoutEngines = true;
                    r_Initialized = true;
                    return;
                }

                InitializeCommunicator();

                var rStartInfo = new ProcessStartInfo()
                {
                    FileName = typeof(BrowserService).Assembly.Location,
                    Arguments = $"browser {Preference.Instance.Browser.CurrentLayoutEngine} {HostProcessID}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                r_BrowserProcess = Process.Start(rStartInfo);
                r_BrowserProcess.BeginOutputReadLine();
                r_BrowserProcess.OutputDataReceived += (s, e) => Trace.WriteLine(e.Data);

                Messages.Subscribe(CommunicatorMessages.Ready, _ => Communicator.Write(CommunicatorMessages.SetPort + ":" + Preference.Instance.Network.Port));
                Messages.SubscribeOnDispatcher(CommunicatorMessages.Attach, rpHandle => Attach((IntPtr)int.Parse(rpHandle)));

                r_Initialized = true;

                Messages.Subscribe(CommunicatorMessages.LoadCompleted, _ => Communicator.Write(CommunicatorMessages.SetZoom + ":" + Preference.Instance.Browser.Zoom));
                Messages.Subscribe(CommunicatorMessages.LoadGamePageCompleted, _ => ResizeBrowserToFitGame());

                Navigator = new BrowserNavigator(this);
                GameController = new GameController(this);
            }
        }
        bool LoadLayoutEngines()
        {
            var rBrowsersDirectory = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), "Browsers"));
            if (!rBrowsersDirectory.Exists)
                return false;

            try
            {
                var rInstalledLayoutEngines = rBrowsersDirectory.EnumerateFiles("*.json").Select(r =>
                {
                    using (var rReader = new JsonTextReader(File.OpenText(r.FullName)))
                        return JObject.Load(rReader).ToObject<LayoutEngineInfo>();
                });
                InstalledLayoutEngines = rInstalledLayoutEngines.ToList();

                if (InstalledLayoutEngines.Count == 0)
                    return false;

                var rSelectedLayoutEngine = Preference.Instance.Browser.CurrentLayoutEngine.Value;
                if (InstalledLayoutEngines.FirstOrDefault(r => r.Name == rSelectedLayoutEngine) == null)
                    Preference.Instance.Browser.CurrentLayoutEngine.Value = InstalledLayoutEngines[0].Name;
            }
            catch
            {
                return false;
            }

            return true;
        }
        void InitializeCommunicator()
        {
            Communicator = new MemoryMappedFileCommunicator($"Sakuno/HeavenlyWind({HostProcessID})", 4096);
            Communicator.ReadPosition = 2048;
            Communicator.WritePosition = 0;

            Messages = Communicator.GetMessageObservable().Publish();
            Messages.Connect();

            Communicator.StartReader();
        }

        async void Attach(IntPtr rpHandle)
        {
            BrowserControl = new BrowserHost(rpHandle);
            OnPropertyChanged(nameof(BrowserControl));

            Attached?.Invoke();

            BrowserControl.SizeChanged += (s, e) => Resized?.Invoke(this, e.NewSize);

            await Task.Delay(2000);

            Navigator.Navigate(Preference.Instance.Browser.Homepage);
        }

        internal async void ResizeBrowserToFitGame()
        {
            if (BrowserControl == null)
                return;

            var rDispatcher = BrowserControl.Dispatcher;

            Communicator.Write(CommunicatorMessages.ResizeBrowserToFitGame);
            IsNavigatorVisible = false;

            await rDispatcher.InvokeAsync(BrowserControl.ResizeBrowserToFitGame);

            IsResizedToFitGame = true;
            OnPropertyChanged(nameof(IsResizedToFitGame));

            var rDesiredSize = await rDispatcher.InvokeAsync(() => BrowserControl.DesiredSize);
            Resized?.Invoke(this, rDesiredSize);

            ResizedToFitGame?.Invoke();
        }

        public IDisposable RegisterMessageObserver(string rpMessage, Action<string> rpObserver) =>
            Messages.Where(r => r.Key == rpMessage).Subscribe(r => rpObserver(r.Value));

        public void SetDefaultHomepage(string rpUrl) => Preference.Instance.Browser.Homepage.Value = rpUrl;
    }
}
