using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Configuration;

namespace MusicStemPlayer
{
    public partial class MainForm : Form
    {
        private Button btnSelectFolder;
        private Button btnPlay;
        private Button btnPause;
        private Button btnRestart;
        private TrackBar progressBar;
        private Label timeLabel;
        private Timer updateTimer;
        private ListBox debugListBox;

        private List<TrackPlayer> trackPlayers = new List<TrackPlayer>();
        private MixingSampleProvider mixer;
        private IWavePlayer waveOut;
        private bool isPlaying = false;

        public MainForm()
        {
            InitializeComponent();
            InitializeControls();
        }

        private void InitializeControls()
        {
            this.ClientSize = new System.Drawing.Size(800, 600);

            btnSelectFolder = new Button { Text = "Select Folder", Location = new System.Drawing.Point(10, 10), Width = 100 };
            btnSelectFolder.Click += BtnSelectFolder_Click;
            Controls.Add(btnSelectFolder);

            btnPlay = new Button { Text = "Play", Location = new System.Drawing.Point(120, 10), Width = 80, Enabled = false };
            btnPlay.Click += BtnPlay_Click;
            Controls.Add(btnPlay);

            btnPause = new Button { Text = "Pause", Location = new System.Drawing.Point(210, 10), Width = 80, Enabled = false };
            btnPause.Click += BtnPause_Click;
            Controls.Add(btnPause);

            btnRestart = new Button { Text = "Restart", Location = new System.Drawing.Point(300, 10), Width = 80, Enabled = false };
            btnRestart.Click += BtnRestart_Click;
            Controls.Add(btnRestart);

            progressBar = new TrackBar { Location = new System.Drawing.Point(10, 40), Width = this.ClientSize.Width - 20, Enabled = false };
            progressBar.Scroll += ProgressBar_Scroll;
            Controls.Add(progressBar);

            timeLabel = new Label { Location = new System.Drawing.Point(10, 70), AutoSize = true };
            Controls.Add(timeLabel);

            //debugListBox = new ListBox { Location = new System.Drawing.Point(10, 400), Width = this.ClientSize.Width - 20, Height = 150 };
            //Controls.Add(debugListBox);

            updateTimer = new Timer { Interval = 100 };
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            using (var folderBrowser = new FolderBrowserDialog())
            {
                // Set initial directory to last used folder
                string lastFolder = ConfigurationManager.AppSettings["LastFolder"];
                if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
                {
                    folderBrowser.SelectedPath = lastFolder;
                }

                if (folderBrowser.ShowDialog() == DialogResult.OK)
                {
                    LoadAudioFiles(folderBrowser.SelectedPath);

                    // Save the selected folder
                    var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    config.AppSettings.Settings["LastFolder"].Value = folderBrowser.SelectedPath;
                    config.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("appSettings");
                }
            }
        }

        private void LoadAudioFiles(string folderPath)
        {
            ClearCurrentTracks();

            var audioFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(file => file.ToLower().EndsWith(".mp3") || file.ToLower().EndsWith(".flac"))
                .ToList();

            int yPosition = 100;
            for (int i = 0; i < audioFiles.Count; i++)
            {
                var file = audioFiles[i];
                var trackPlayer = new TrackPlayer(file, i);
                trackPlayers.Add(trackPlayer);

                var trackControl = new TrackControl(Path.GetFileName(file), yPosition, this.ClientSize.Width, i);
                trackControl.VolumeChanged += TrackControl_VolumeChanged;
                Controls.Add(trackControl);

                yPosition += 30;
            }

            if (trackPlayers.Any())
            {
                mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
                foreach (var player in trackPlayers)
                {
                    mixer.AddMixerInput(player.GetSampleProvider());
                }

                waveOut = new WaveOutEvent();
                waveOut.Init(mixer);

                btnPlay.Enabled = btnPause.Enabled = btnRestart.Enabled = progressBar.Enabled = true;
                progressBar.Maximum = (int)trackPlayers[0].TotalTime.TotalSeconds;
                UpdateTimeLabel();
            }

            this.ClientSize = new System.Drawing.Size(this.ClientSize.Width, Math.Max(yPosition + 50, 600));
        }

        private void TrackControl_VolumeChanged(object sender, TrackVolumeChangedEventArgs e)
        {
            if (e.TrackIndex >= 0 && e.TrackIndex < trackPlayers.Count)
            {
                trackPlayers[e.TrackIndex].SetVolume(e.Volume);
                //DebugLog($"Track {e.TrackIndex} volume changed to {e.Volume}");
            }
        }

        private void ClearCurrentTracks()
        {
            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }

            foreach (var player in trackPlayers)
            {
                player.Dispose();
            }
            trackPlayers.Clear();

            foreach (var control in Controls.OfType<TrackControl>().ToList())
            {
                Controls.Remove(control);
                control.Dispose();
            }

            mixer = null;
            isPlaying = false;
            updateTimer.Stop();
            //debugListBox.Items.Clear();
        }

        private void BtnPlay_Click(object sender, EventArgs e)
        {
            waveOut.Play();
            isPlaying = true;
            updateTimer.Start();
            //DebugLog("Playback started");
        }

        private void BtnPause_Click(object sender, EventArgs e)
        {
            waveOut.Pause();
            isPlaying = false;
            updateTimer.Stop();
            //DebugLog("Playback paused");
        }

        private void BtnRestart_Click(object sender, EventArgs e)
        {
            waveOut.Stop();
            foreach (var player in trackPlayers)
            {
                player.Restart();
            }
            progressBar.Value = 0;
            UpdateTimeLabel();
            //DebugLog("Playback restarted");
            if (!isPlaying)
            {
                BtnPlay_Click(sender, e);
            }
        }

        private void ProgressBar_Scroll(object sender, EventArgs e)
        {
            var newPosition = TimeSpan.FromSeconds(progressBar.Value);
            foreach (var player in trackPlayers)
            {
                player.SetPosition(newPosition);
            }
            UpdateTimeLabel();
            //DebugLog($"Seek to position: {newPosition}");
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (trackPlayers.Any())
            {
                progressBar.Value = (int)trackPlayers[0].CurrentTime.TotalSeconds;
                UpdateTimeLabel();
            }
        }

        private void UpdateTimeLabel()
        {
            if (trackPlayers.Any())
            {
                var currentTime = trackPlayers[0].CurrentTime;
                var totalTime = trackPlayers[0].TotalTime;
                timeLabel.Text = $"{currentTime:mm\\:ss} / {totalTime:mm\\:ss}";
            }
        }

        //       private void DebugLog(string message)
        //       {
        //           debugListBox.Items.Add($"{DateTime.Now:HH:mm:ss.fff}: {message}");
        //           debugListBox.TopIndex = debugListBox.Items.Count - 1;
        //       }
        //    }

        public class TrackPlayer : IDisposable
        {
            private AudioFileReader audioFile;
            private VolumeSampleProvider volumeProvider;
            public int Index { get; }

            public TrackPlayer(string filePath, int index)
            {
                Index = index;
                audioFile = new AudioFileReader(filePath);
                volumeProvider = new VolumeSampleProvider(audioFile);
            }

            public TimeSpan CurrentTime => audioFile.CurrentTime;
            public TimeSpan TotalTime => audioFile.TotalTime;

            public void Restart() => audioFile.Position = 0;
            public void SetPosition(TimeSpan position) => audioFile.CurrentTime = position;
            public void SetVolume(float volume) => volumeProvider.Volume = volume;

            public ISampleProvider GetSampleProvider() => volumeProvider;

            public void Dispose()
            {
                audioFile.Dispose();
            }
        }

        public class TrackControl : Panel
        {
            public event EventHandler<TrackVolumeChangedEventArgs> VolumeChanged;
            private int trackIndex;

            public TrackControl(string name, int yPosition, int parentWidth, int index)
            {
                trackIndex = index;
                var label = new Label { Text = name, AutoSize = true, Location = new System.Drawing.Point(10, 5) };
                var trackBar = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, Width = 200, Location = new System.Drawing.Point(parentWidth - 220, 0) };

                trackBar.ValueChanged += (s, e) => VolumeChanged?.Invoke(this, new TrackVolumeChangedEventArgs(trackIndex, trackBar.Value / 100f));

                Controls.Add(label);
                Controls.Add(trackBar);

                Location = new System.Drawing.Point(10, yPosition);
                Width = parentWidth - 20;
                Height = 30;
            }
        }

        public class TrackVolumeChangedEventArgs : EventArgs
        {
            public int TrackIndex { get; }
            public float Volume { get; }

            public TrackVolumeChangedEventArgs(int trackIndex, float volume)
            {
                TrackIndex = trackIndex;
                Volume = volume;
            }
        }
    }
}