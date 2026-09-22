// Stratum.Windows - row view-model for the authenticator list.

using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Stratum.Core;
using Stratum.Core.Entity;
using Stratum.Core.Generator;
using Stratum.Windows.Services;

namespace Stratum.Windows.Models
{
    public class AuthenticatorRow : INotifyPropertyChanged
    {
        public Authenticator Authenticator { get; }
        public string CategoryNames { get; set; } = "";
        public BitmapImage CustomIcon { get; set; }

        private string _code = "------";
        private string _displayCode = "------";
        private double _progressValue;
        private long _lastPeriodIndex = -1;
        private int _period = 30;
        private bool _isHotp;
        private DateTime _revealUntilUtc = DateTime.MinValue;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Display options, synced from SettingsStore by the page.</summary>
        public static bool ShowUsernames { get; set; } = true;
        public static string GroupingMode { get; set; } = "Halves";
        public static bool TapToRevealEnabled { get; set; } = false;
        public static TimeSpan RevealDuration { get; set; } = TimeSpan.FromSeconds(10);
        public static bool SkipToNextEnabled { get; set; } = false;

        private SolidColorBrush _avatarBrush;
        private string _avatarLetter;

        public AuthenticatorRow(Authenticator auth)
        {
            Authenticator = auth;
            _isHotp = auth.Type.GetGenerationMethod() == GenerationMethod.Counter;
            _period = auth.Type == AuthenticatorType.Totp ? auth.Period : 30;
            _avatarBrush = UiHelpers.AvatarBrush(auth.Issuer);
            _avatarLetter = UiHelpers.AvatarLetter(auth.Issuer);
        }

        public SolidColorBrush AvatarBrush => _avatarBrush;
        public string AvatarLetter => _avatarLetter;
        public bool ShowUsername => ShowUsernames && !string.IsNullOrEmpty(Username);

        public string Issuer => Authenticator.Issuer;
        public string Username => Authenticator.Username;

        public bool IsHotp => _isHotp;
        public int Period => _period;

        /// <summary>Real code (clipboard/copy). Never masked.</summary>
        public string Code
        {
            get => _code;
            set { _code = value; OnPropertyChanged(); UpdateDisplayCode(); }
        }

        /// <summary>What the card shows (masked when tap-to-reveal is on).</summary>
        public string DisplayCode
        {
            get => _displayCode;
            private set { _displayCode = value; OnPropertyChanged(); }
        }

        public bool IsRevealed => !TapToRevealEnabled || DateTime.UtcNow < _revealUntilUtc;

        public void Reveal()
        {
            _revealUntilUtc = DateTime.UtcNow + RevealDuration;
            UpdateDisplayCode();
        }

        public int SecondsRemaining => (int)Math.Ceiling(ProgressValue);

        /// <summary>Segundos restantes fracionários — alimenta a barra com animação suave.</summary>
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public void Tick()
        {
            if (IsHotp)
            {
                UpdateDisplayCode();
                return;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var periodMs = (long)Period * 1000;
            var index = nowMs / periodMs;
            var remainingMs = periodMs - (nowMs % periodMs);

            ProgressValue = remainingMs / 1000.0;

            if (index != _lastPeriodIndex || Code == "------")
            {
                _lastPeriodIndex = index;

                try
                {
                    string code;

                    if (SkipToNextEnabled && remainingMs < periodMs / 3)
                    {
                        var nextCounter = (index + 1) * Period;
                        code = Authenticator.GetCode(nextCounter);
                    }
                    else
                    {
                        code = Authenticator.GetCode();
                    }

                    Code = Group(code);
                }
                catch
                {
                    Code = "erro";
                }
            }
            else
            {
                UpdateDisplayCode();
            }
        }

        public void RefreshHotpCode()
        {
            try
            {
                Code = Group(Authenticator.GetCode());
            }
            catch
            {
                Code = "erro";
            }
        }

        private void UpdateDisplayCode()
        {
            if (!TapToRevealEnabled || DateTime.UtcNow < _revealUntilUtc)
            {
                DisplayCode = Code;
                return;
            }

            DisplayCode = Code == "erro" || Code == "------"
                ? Code
                : new string('•', Authenticator.Digits);
        }

        private static string Group(string code)
        {
            try
            {
                if (string.IsNullOrEmpty(code) || !code.All(char.IsDigit))
                    return code;

                switch (GroupingMode)
                {
                    case "None":
                        return code;
                    case "Two":
                        return Chunk(code, 2);
                    case "Three":
                        return Chunk(code, 3);
                    case "Four":
                        return Chunk(code, 4);
                    case "Thirds" when code.Length % 3 == 0:
                        return Chunk(code, code.Length / 3);
                    case "Halves":
                    default:
                        return code.Length % 2 == 0 && (code.Length == 6 || code.Length == 8)
                            ? code.Insert(code.Length / 2, " ")
                            : code;
                }
            }
            catch
            {
                return code;
            }
        }

        private static string Chunk(string code, int size)
        {
            if (size <= 0 || code.Length <= size)
                return code;

            var builder = new StringBuilder();
            for (var i = 0; i < code.Length; i += size)
            {
                if (i > 0)
                    builder.Append(' ');
                builder.Append(code.Substring(i, Math.Min(size, code.Length - i)));
            }

            return builder.ToString();
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
