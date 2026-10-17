using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;

namespace AudioMonitor;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _meterTimer;
    private readonly double[] _recentPeaks = new double[3];
    private readonly object _lock = new();

    private WaveInEvent? _waveIn;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _bufferedWaveProvider;

    private bool _isPaused;
    private bool _isMuted;
    private bool _clippingToneActive;
    private double _inputLevel;

    public MainWindow()
    {
        InitializeComponent();

        _meterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _meterTimer.Tick += MeterTimer_Tick;

        Loaded += (_, _) =>
        {
            StartMonitoring();
            // Zorg dat het venster keyboard focus krijgt zodat spatiebalk werkt
            Keyboard.Focus(this);
        };
        Closed += (_, _) => StopMonitoring();

        AutomationProperties.SetName(this, "Audio Monitor");
        AutomationProperties.SetHelpText(this, "Monitors microphone input and plays it to the output device while showing VU meter levels. Gebruik de spatiebalk om te pauzeren/te hervatten.");
    }

    private void StartMonitoring()
    {
        try
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(44100, 2),
                BufferMilliseconds = 50,
                NumberOfBuffers = 4
            };

            _waveIn.DataAvailable += WaveIn_DataAvailable;
            _waveIn.StartRecording();

            _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 2))
            {
                BufferDuration = TimeSpan.FromMilliseconds(200)
            };

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_bufferedWaveProvider);
            _waveOut.Play();

            StatusText.Text = "Monitoring";
            _meterTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Audio error: {ex.Message}";
            Debug.WriteLine(ex);
        }
    }

    private void StopMonitoring()
    {
        _meterTimer.Stop();

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isPaused || _isMuted)
        {
            return;
        }

        if (_bufferedWaveProvider is null)
        {
            return;
        }

        lock (_lock)
        {
            _bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

            var sampleCount = e.BytesRecorded / 2;
            var peak = 0.0d;

            for (var i = 0; i < sampleCount; i++)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i * 2);
                var normalized = Math.Abs((double)sample / short.MaxValue);
                if (normalized > peak)
                {
                    peak = normalized;
                }
            }

            _inputLevel = peak;
            _recentPeaks[0] = _recentPeaks[1];
            _recentPeaks[1] = _recentPeaks[2];
            _recentPeaks[2] = peak;
        }
    }

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        var currentPeak = _recentPeaks[2];

        if (_isPaused)
        {
            currentPeak = 0.0d;
            StatusText.Text = "Paused";
        }
        else if (_isMuted)
        {
            currentPeak = 0.0d;
            StatusText.Text = "Muted";
        }
        else
        {
            StatusText.Text = "Monitoring";
        }

        UpdateMeter(MeterLeft, currentPeak, 0.80d);
        UpdateMeter(MeterCenter, currentPeak * 0.85d, 0.78d);
        UpdateMeter(MeterRight, currentPeak * 0.92d, 0.82d);

        if (!_isPaused && !_isMuted && currentPeak > 0.82d && !_clippingToneActive)
        {
            _clippingToneActive = true;
            System.Media.SystemSounds.Beep.Play();
        }
        else if (currentPeak <= 0.75d)
        {
            _clippingToneActive = false;
        }
    }

    private static void UpdateMeter(Rectangle meter, double level, double clipThreshold)
    {
        var clampedLevel = Math.Min(1.0d, Math.Max(0.0d, level));
        var targetHeight = 220d * clampedLevel;
        meter.Height = targetHeight;

        if (clampedLevel > clipThreshold)
        {
            meter.Fill = Brushes.Red;
        }
        else
        {
            meter.Fill = Brushes.White;
        }
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        // Update status text immediately; MeterTimer_Tick will also update
        StatusText.Text = _isPaused ? "Paused" : "Monitoring";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            TogglePause();
            e.Handled = true;
        }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _isMuted = !_isMuted;
        MuteButton.Content = _isMuted ? "Unmute" : "Mute";
        if (!_isMuted)
        {
            StatusText.Text = "Monitoring";
        }
    }
}
