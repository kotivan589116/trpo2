using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SharpVectors.Converters;

namespace WpfApp1
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const double CoinSize = 48;
        private const double SlotCenterX = 329;
        private const double SlotCenterY = 314;

        // Монета уменьшается, «входя» в монетоприёмник.
        private const double SlotCoinScale = 0.5;

        // Сдача: монеты лежат горкой в лотке ( координаты внутри автомата ).
        private const double TrayCenterX = 335;
        private const double TrayFloorY = 433;
        private const double TrayCoinSize = 14;
        private const double TrayCoinSpacing = 7.5;
        private const double TrayRowStep = 7.0;
        private const double TrayDropStartY = 395;
        private const int TrayCapacity = 10;
        private const double OverflowFallDistance = 480;

        private const double CoinOneHomeX = 450;
        private const double CoinTwoHomeX = 510;
        private const double CoinHomeY = 290;

        // Область автомата, которая должна занять весь экран при зуме.
        private const double ZoomFocusX = 198;
        private const double ZoomFocusY = 431.5;
        private const double ZoomFocusHeight = 300;

        private const double GlassForwardScale = 1.7;
        private const double GlassForwardX = 47;
        private const double GlassForwardY = -93;

        // Позиция стакана при промывке (сдвиг вправо по канве).
        private const double GlassRinseX = 100;

        private sealed class Drink
        {
            public Drink(string name, int price, Border button,
                         SvgViewbox normalPlate, SvgViewbox litPlate,
                         Color liquidColor, Color streamColor)
            {
                Name = name;
                Price = price;
                Button = button;
                NormalPlate = normalPlate;
                LitPlate = litPlate;
                LiquidBrush = new SolidColorBrush(liquidColor);
                StreamBrush = new SolidColorBrush(streamColor);
            }

            public string Name { get; }
            public int Price { get; }
            public Border Button { get; }
            public SvgViewbox NormalPlate { get; }
            public SvgViewbox LitPlate { get; }
            public Brush LiquidBrush { get; }
            public Brush StreamBrush { get; }
        }

        private sealed class TrayCoin
        {
            public int Value;
            public Grid Element;
            public ScaleTransform Scale;
            public RotateTransform Rotate;
        }

        private readonly List<Drink> _drinks = new List<Drink>();
        private readonly List<int> _insertedCoins = new List<int>();
        private readonly List<TrayCoin> _trayCoins = new List<TrayCoin>();
        private readonly Random _random = new Random();

        private int _credit;
        private bool _busy;
        private bool _zoomed;
        private bool _glassFull;

        private double _zoomScale = 1;
        private double _zoomTx;
        private double _zoomTy;

        public MainWindow()
        {
            InitializeComponent();

            _drinks.Add(new Drink("Буратино", 3, BtnBuratino,
                PlateBuratino, PlateBuratinoLit,
                Color.FromArgb(235, 0xF2, 0xA5, 0x2E), Color.FromRgb(0xED, 0x94, 0x12)));
            _drinks.Add(new Drink("Тархун", 3, BtnTarhun,
                PlateTarhun, PlateTarhunLit,
                Color.FromArgb(235, 0x6C, 0xC0, 0x4A), Color.FromRgb(0x4F, 0xB0, 0x34)));
            _drinks.Add(new Drink("Без сиропа", 1, BtnNoSyrup,
                PlateNoSyrup, PlateNoSyrupLit,
                Color.FromArgb(220, 0xC5, 0xE7, 0xF5), Color.FromRgb(0x8F, 0xD4, 0xF0)));

            UpdateState();
        }

        private void UpdateState()
        {
            foreach (Drink drink in _drinks)
            {
                bool ready = !_busy && !_glassFull && _credit >= drink.Price;

                drink.Button.Opacity = ready ? 1.0 : 0.55;
                drink.Button.Cursor = ready ? Cursors.Hand : Cursors.Arrow;
            }

            // Стакан всегда доступен, если автомат не занят:
            // пустой — промываем, полный — пьём. Монеты и зум не мешают.
            GlassGroup.Cursor = !_busy ? Cursors.Hand : Cursors.Arrow;
            GlassGroup.IsHitTestVisible = !_busy;

            // Забрать сдачу можно, только если в лотке есть монеты.
            bool canTakeChange = !_busy && _trayCoins.Count > 0;
            ChangeTray.Cursor = canTakeChange ? Cursors.Hand : Cursors.Arrow;
            ChangeTray.ToolTip = canTakeChange ? "Забрать сдачу" : null;
        }

        #region Камера (зум автомата)

        /// <summary>
        /// Считает масштаб и сдвиг, при которых центральная часть автомата
        /// (плашки, кнопки, монетоприёмник и стакан) занимает весь экран.
        /// </summary>
        private void ComputeZoomTarget()
        {
            double width = SceneRoot.ActualWidth;
            double height = SceneRoot.ActualHeight;

            if (width < 10 || height < 10)
            {
                _zoomScale = 1;
                _zoomTx = 0;
                _zoomTy = 0;
                return;
            }

            // Уменьшаем зум так, чтобы монеты (они масштабируются вместе
            // с автоматом) целиком влезали в поле справа от корпуса.
            // 396*S + 2*(48*S + 10) <= width  =>  S <= (width - 40) / 492.
            double scale = Math.Min((width - 40.0) / 492.0, height / ZoomFocusHeight);
            _zoomScale = Math.Max(1.0, Math.Min(scale, 4.0));

            double layoutLeft = (width - 396.0) / 2.0;
            double layoutTop = (height - 938.0) / 2.0;

            // Точка фокуса должна попасть в центр экрана.
            _zoomTx = width / 2.0 - layoutLeft - _zoomScale * ZoomFocusX;
            _zoomTy = height / 2.0 - layoutTop - _zoomScale * ZoomFocusY;
        }

        private void AnimateCamera(double toScale, double toTx, double toTy, int milliseconds)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var duration = TimeSpan.FromMilliseconds(milliseconds);

            MachineScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(MachineScale.ScaleX, toScale, duration) { EasingFunction = ease });
            MachineScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(MachineScale.ScaleY, toScale, duration) { EasingFunction = ease });
            MachineTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(MachineTranslate.X, toTx, duration) { EasingFunction = ease });
            MachineTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(MachineTranslate.Y, toTy, duration) { EasingFunction = ease });
        }

        private void SetCameraValues(double scale, double tx, double ty)
        {
            MachineScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            MachineScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            MachineTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            MachineTranslate.BeginAnimation(TranslateTransform.YProperty, null);

            MachineScale.ScaleX = scale;
            MachineScale.ScaleY = scale;
            MachineTranslate.X = tx;
            MachineTranslate.Y = ty;
        }

        private async Task ZoomInAsync()
        {
            if (_zoomed)
            {
                return;
            }

            _zoomed = true;
            ComputeZoomTarget();

            AnimateCamera(_zoomScale, _zoomTx, _zoomTy, 600);
            AnimateCoinsToDock(true, 600);

            await Task.Delay(650);

            SetCameraValues(_zoomScale, _zoomTx, _zoomTy);
            SetCoinBase(true);
        }

        private async Task ZoomOutAsync()
        {
            if (!_zoomed)
            {
                return;
            }

            AnimateCamera(1, 0, 0, 600);
            AnimateCoinsToDock(false, 600);

            await Task.Delay(650);

            SetCameraValues(1, 0, 0);
            SetCoinBase(false);
            _zoomed = false;
        }

        private void OnSceneRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (MachineScale == null || CoinOne == null)
            {
                return;
            }

            if (!_zoomed || _busy)
            {
                return;
            }

            ComputeZoomTarget();
            SetCameraValues(_zoomScale, _zoomTx, _zoomTy);
            SetCoinBase(true);
        }

        #endregion

        #region Монеты

        private async void OnCoinOneClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await InsertCoinAsync(1, CoinOne, CoinOneMove, CoinOneScale);
        }

        private async void OnCoinTwoClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await InsertCoinAsync(3, CoinTwo, CoinTwoMove, CoinTwoScale);
        }

        /// <summary>
        /// При первой монете автомат приближается. Затем монета летит в щель.
        /// </summary>
        private async Task InsertCoinAsync(int value, Grid coin,
                                           TranslateTransform move, ScaleTransform scale)
        {
            if (_busy || !coin.IsEnabled)
            {
                return;
            }

            coin.IsEnabled = false;

            if (!_zoomed)
            {
                _busy = true;
                UpdateState();

                await ZoomInAsync();

                _busy = false;
                UpdateState();
            }

            StartCoinFlight(value, coin, move, scale);
        }

        private void StartCoinFlight(int value, Grid coin,
                                     TranslateTransform move, ScaleTransform scale)
        {
            double cameraScale = _zoomed ? _zoomScale : 1.0;
            double cameraTx = _zoomed ? _zoomTx : 0.0;
            double cameraTy = _zoomed ? _zoomTy : 0.0;

            double targetCenterX = SlotCenterX * cameraScale + cameraTx;
            double targetCenterY = SlotCenterY * cameraScale + cameraTy;

            double baseLeft = Canvas.GetLeft(coin);
            double baseTop = Canvas.GetTop(coin);

            double offsetX = targetCenterX - (baseLeft + CoinSize / 2.0);
            double offsetY = targetCenterY - (baseTop + CoinSize / 2.0);

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var slideX = new DoubleAnimation(0, offsetX, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = ease
            };
            var slideY = new DoubleAnimation(0, offsetY, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = ease
            };

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
            {
                BeginTime = TimeSpan.FromMilliseconds(460)
            };

            // Монета уменьшается, чтобы «пролезть» в отверстие монетоприёмника.
            var shrinkX = new DoubleAnimation(1, SlotCoinScale, TimeSpan.FromMilliseconds(560))
            {
                EasingFunction = ease
            };
            var shrinkY = new DoubleAnimation(1, SlotCoinScale, TimeSpan.FromMilliseconds(560))
            {
                EasingFunction = ease
            };

            slideX.Completed += delegate { FinishCoinInsert(value, coin, move, scale); };

            move.BeginAnimation(TranslateTransform.XProperty, slideX);
            move.BeginAnimation(TranslateTransform.YProperty, slideY);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
            coin.BeginAnimation(OpacityProperty, fade);
        }

        private void FinishCoinInsert(int value, Grid coin, TranslateTransform move, ScaleTransform scale)
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            coin.BeginAnimation(OpacityProperty, null);

            move.X = 0;
            move.Y = 0;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            coin.Opacity = 1;
            coin.IsEnabled = true;

            _credit += value;
            _insertedCoins.Add(value);
            UpdateState();
        }

        /// <summary>
        /// Возврат: все внесённые монеты падают в лоток сдачи и остаются там горкой.
        /// </summary>
        private async void OnReturnClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await ReturnCoinsAsync();
        }

        private async Task ReturnCoinsAsync()
        {
            if (_busy || _credit <= 0)
            {
                return;
            }

            _busy = true;
            UpdateState();

            var coins = new List<int>(_insertedCoins);
            if (coins.Count == 0)
            {
                coins = Decompose(_credit);
            }

            _insertedCoins.Clear();
            _credit = 0;
            UpdateState();

            for (int i = 0; i < coins.Count; i++)
            {
                SpawnTrayCoin(coins[i], i * 130);
            }

            await Task.Delay(coins.Count * 130 + 900);

            _busy = false;
            UpdateState();
        }

        private static List<int> Decompose(int amount)
        {
            var coins = new List<int>();

            while (amount >= 3)
            {
                coins.Add(3);
                amount -= 3;
            }
            while (amount > 0)
            {
                coins.Add(1);
                amount -= 1;
            }

            return coins;
        }

        #endregion

        #region Сдача

        /// <summary>
        /// Монета падает в лоток сдачи. Пока в лотке меньше 10 монет, она
        /// остаётся лежать в горке; лишние монеты проскакивают лоток и
        /// падают вниз, как раньше.
        /// </summary>
        private void SpawnTrayCoin(int value, int delayMs)
        {
            if (_trayCoins.Count >= TrayCapacity)
            {
                SpawnOverflowCoin(value, delayMs);
                return;
            }

            double left, top, angle;
            GetTraySlot(_trayCoins.Count, out left, out top);
            angle = (_random.NextDouble() * 2 - 1) * 14.0;

            double startLeft = TrayCenterX - TrayCoinSize / 2.0
                               + (_random.NextDouble() * 2 - 1) * 3.0;
            double startTop = TrayDropStartY - TrayCoinSize / 2.0;

            var scale = new ScaleTransform(0.85, 0.85);
            var rotate = new RotateTransform(angle + (_random.NextDouble() * 2 - 1) * 25.0);
            var transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(rotate);

            var coin = new Grid
            {
                Width = TrayCoinSize,
                Height = TrayCoinSize,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = transforms
            };
            coin.Children.Add(new SvgViewbox
            {
                Source = new Uri(value == 3 ? "/Image/m2.svg" : "/Image/m1.svg", UriKind.Relative),
                Stretch = Stretch.Fill
            });

            Canvas.SetLeft(coin, startLeft);
            Canvas.SetTop(coin, startTop);
            MachineCanvas.Children.Add(coin);

            var begin = TimeSpan.FromMilliseconds(delayMs);
            var duration = TimeSpan.FromMilliseconds(430);
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var dropX = new DoubleAnimation(startLeft, left, duration)
            {
                BeginTime = begin,
                EasingFunction = easeOut
            };
            var dropY = new DoubleAnimation(startTop, top, duration)
            {
                BeginTime = begin,
                EasingFunction = new BounceEase
                {
                    Bounces = 1,
                    Bounciness = 2,
                    EasingMode = EasingMode.EaseOut
                }
            };
            var appear = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90))
            {
                BeginTime = begin
            };
            var settleX = new DoubleAnimation(0.85, 1, duration)
            {
                BeginTime = begin,
                EasingFunction = easeOut
            };
            var settleY = new DoubleAnimation(0.85, 1, duration)
            {
                BeginTime = begin,
                EasingFunction = easeOut
            };
            var spin = new DoubleAnimation(rotate.Angle, angle, duration)
            {
                BeginTime = begin,
                EasingFunction = easeOut
            };

            _trayCoins.Add(new TrayCoin
            {
                Value = value,
                Element = coin,
                Scale = scale,
                Rotate = rotate
            });

            coin.BeginAnimation(Canvas.LeftProperty, dropX);
            coin.BeginAnimation(Canvas.TopProperty, dropY);
            coin.BeginAnimation(OpacityProperty, appear);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, settleX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, settleY);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        /// <summary>
        /// Раскладывает монеты горкой: 5 в нижнем ряду, 3 в среднем и 2 в верхнем.
        /// </summary>
        private static void GetTraySlot(int index, out double left, out double top)
        {
            const int bottomRow = 5;
            const int middleRow = 3;

            double dx;
            double dy;

            if (index < bottomRow)
            {
                dx = (index - 2) * TrayCoinSpacing;
                dy = 0;
            }
            else if (index < bottomRow + middleRow)
            {
                int i = index - bottomRow;
                dx = (i - 1) * TrayCoinSpacing;
                dy = -TrayRowStep;
            }
            else
            {
                int i = index - bottomRow - middleRow;
                dx = (i - 0.5) * TrayCoinSpacing;
                dy = -TrayRowStep * 2;
            }

            left = TrayCenterX + dx - TrayCoinSize / 2.0;
            top = TrayFloorY - TrayCoinSize / 2.0 + dy;
        }

        /// <summary>
        /// Лоток переполнен: монета вываливается из него и падает вниз.
        /// </summary>
        private void SpawnOverflowCoin(int value, int delayMs)
        {
            var coin = new Grid
            {
                Width = TrayCoinSize,
                Height = TrayCoinSize,
                IsHitTestVisible = false
            };
            coin.Children.Add(new SvgViewbox
            {
                Source = new Uri(value == 3 ? "/Image/m2.svg" : "/Image/m1.svg", UriKind.Relative),
                Stretch = Stretch.Fill
            });

            Canvas.SetLeft(coin, TrayCenterX - TrayCoinSize / 2.0
                                 + (_random.NextDouble() * 2 - 1) * 6.0);
            Canvas.SetTop(coin, TrayFloorY - TrayCoinSize / 2.0);

            var move = new TranslateTransform();
            coin.RenderTransform = move;
            MachineCanvas.Children.Add(coin);

            var begin = TimeSpan.FromMilliseconds(delayMs);

            var fall = new DoubleAnimation(0, OverflowFallDistance, TimeSpan.FromMilliseconds(820))
            {
                BeginTime = begin,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fall.Completed += delegate { MachineCanvas.Children.Remove(coin); };

            var fade = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = begin
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(820))));

            move.BeginAnimation(TranslateTransform.YProperty, fall);
            coin.BeginAnimation(OpacityProperty, fade);
        }

        private async void OnChangeTrayClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await TakeChangeAsync();
        }

        /// <summary>
        /// Клик по лотку: монеты разлетаются обратно в свои стопки.
        /// </summary>
        private async Task TakeChangeAsync()
        {
            if (_busy || _trayCoins.Count == 0)
            {
                return;
            }

            _busy = true;
            UpdateState();

            var trayCoins = new List<TrayCoin>(_trayCoins);
            _trayCoins.Clear();
            UpdateState();

            double width = SceneRoot.ActualWidth;
            double height = SceneRoot.ActualHeight;
            double layoutLeft = (width - 396.0) / 2.0;
            double layoutTop = (height - 938.0) / 2.0;
            double cameraScale = _zoomed ? _zoomScale : 1.0;
            double cameraTx = _zoomed ? _zoomTx : 0.0;
            double cameraTy = _zoomed ? _zoomTy : 0.0;

            for (int i = 0; i < trayCoins.Count; i++)
            {
                Grid home = trayCoins[i].Value == 3 ? CoinTwo : CoinOne;

                double sceneX = layoutLeft + Canvas.GetLeft(home) + CoinSize / 2.0;
                double sceneY = layoutTop + Canvas.GetTop(home) + CoinSize / 2.0;

                // Переводим координаты стопки из экранных в координаты автомата.
                double targetLeft = (sceneX - layoutLeft - cameraTx) / cameraScale - TrayCoinSize / 2.0;
                double targetTop = (sceneY - layoutTop - cameraTy) / cameraScale - TrayCoinSize / 2.0;

                FlyTrayCoinToStack(trayCoins[i], targetLeft, targetTop, i * 60);
            }

            await Task.Delay(trayCoins.Count * 60 + 800);

            _busy = false;
            UpdateState();
        }

        private void FlyTrayCoinToStack(TrayCoin coin, double targetLeft, double targetTop, int delayMs)
        {
            double fromLeft = Canvas.GetLeft(coin.Element);
            double fromTop = Canvas.GetTop(coin.Element);

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var duration = TimeSpan.FromMilliseconds(620);

            var moveX = new DoubleAnimation(fromLeft, targetLeft, duration)
            {
                BeginTime = begin,
                EasingFunction = ease
            };

            // Небольшой подскок в начале, чтобы монеты «разлетались», а не ехали.
            var moveY = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = begin
            };
            moveY.KeyFrames.Add(new EasingDoubleKeyFrame(fromTop,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
            moveY.KeyFrames.Add(new EasingDoubleKeyFrame(fromTop - 16,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            moveY.KeyFrames.Add(new EasingDoubleKeyFrame(targetTop,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), ease));

            var growX = new DoubleAnimation(coin.Scale.ScaleX, CoinSize / TrayCoinSize, duration)
            {
                BeginTime = begin,
                EasingFunction = ease
            };
            var growY = new DoubleAnimation(coin.Scale.ScaleY, CoinSize / TrayCoinSize, duration)
            {
                BeginTime = begin,
                EasingFunction = ease
            };
            var spin = new DoubleAnimation(coin.Rotate.Angle, coin.Rotate.Angle + 180, duration)
            {
                BeginTime = begin,
                EasingFunction = ease
            };

            moveX.Completed += delegate
            {
                MachineCanvas.Children.Remove(coin.Element);
                PulseStack(coin.Value);
            };

            coin.Element.BeginAnimation(Canvas.LeftProperty, moveX);
            coin.Element.BeginAnimation(Canvas.TopProperty, moveY);
            coin.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
            coin.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, growY);
            coin.Rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        /// <summary>
        /// Лёгкий «подскок» стопки, когда в неё вернулась монета.
        /// </summary>
        private void PulseStack(int value)
        {
            ScaleTransform stack = value == 3 ? CoinTwoScale : CoinOneScale;

            DoubleAnimationUsingKeyFrames CreatePulse()
            {
                var pulse = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(260)
                };
                pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1,
                    KeyTime.FromTimeSpan(TimeSpan.Zero)));
                pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.16,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
                    new CubicEase { EasingMode = EasingMode.EaseInOut }));
                return pulse;
            }

            stack.BeginAnimation(ScaleTransform.ScaleXProperty, CreatePulse());
            stack.BeginAnimation(ScaleTransform.ScaleYProperty, CreatePulse());
        }

        #endregion

        #region Слой монет (не масштабируется вместе с автоматом)

        private void GetDockPosition(Grid coin, out double left, out double top)
        {
            double width = SceneRoot.ActualWidth;
            double height = SceneRoot.ActualHeight;
            double layoutLeft = (width - 396.0) / 2.0;
            double layoutTop = (height - 938.0) / 2.0;

            // Монета масштабируется вместе с автоматом, её видимая ширина = 48*S.
            // Ставим её вплотную к правому краю корпуса, в свободное поле.
            double half = CoinSize / 2.0 * _zoomScale;
            const double gap = 10.0;
            double machineRight = layoutLeft + _zoomTx + 396.0 * _zoomScale;
            double centerX = machineRight + gap + half;

            // Две монеты — вертикальным столбцом, с зазором 8 px между ними.
            double centerY = height / 2.0 + (coin == CoinOne ? -(half + 4.0) : (half + 4.0));

            left = centerX - layoutLeft - CoinSize / 2.0;
            top = centerY - layoutTop - CoinSize / 2.0;
        }

        private void AnimateCoinsToDock(bool toDock, int milliseconds)
        {
            double dockOneLeft, dockOneTop, dockTwoLeft, dockTwoTop;
            GetDockPosition(CoinOne, out dockOneLeft, out dockOneTop);
            GetDockPosition(CoinTwo, out dockTwoLeft, out dockTwoTop);

            double toScale = toDock ? _zoomScale : 1.0;
            AnimateCoinDockScale(CoinOneDockScale, CoinOneDockScale.ScaleX, toScale, milliseconds);
            AnimateCoinDockScale(CoinTwoDockScale, CoinTwoDockScale.ScaleX, toScale, milliseconds);

            if (toDock)
            {
                AnimateCoinPosition(CoinOne, CoinOneHomeX, CoinHomeY, dockOneLeft, dockOneTop, milliseconds);
                AnimateCoinPosition(CoinTwo, CoinTwoHomeX, CoinHomeY, dockTwoLeft, dockTwoTop, milliseconds);
            }
            else
            {
                AnimateCoinPosition(CoinOne, dockOneLeft, dockOneTop, CoinOneHomeX, CoinHomeY, milliseconds);
                AnimateCoinPosition(CoinTwo, dockTwoLeft, dockTwoTop, CoinTwoHomeX, CoinHomeY, milliseconds);
            }
        }

        private static void AnimateCoinDockScale(ScaleTransform scale, double from, double to, int milliseconds)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var duration = TimeSpan.FromMilliseconds(milliseconds);

            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(from, to, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(from, to, duration) { EasingFunction = ease });
        }

        private static void AnimateCoinPosition(Grid coin,
                                                double fromLeft, double fromTop,
                                                double toLeft, double toTop,
                                                int milliseconds)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var duration = TimeSpan.FromMilliseconds(milliseconds);

            coin.BeginAnimation(Canvas.LeftProperty,
                new DoubleAnimation(fromLeft, toLeft, duration) { EasingFunction = ease });
            coin.BeginAnimation(Canvas.TopProperty,
                new DoubleAnimation(fromTop, toTop, duration) { EasingFunction = ease });
        }

        private void SetCoinBase(bool docked)
        {
            SetCoinBase(CoinOne, docked, CoinOneHomeX, CoinOneDockScale);
            SetCoinBase(CoinTwo, docked, CoinTwoHomeX, CoinTwoDockScale);
        }

        private void SetCoinBase(Grid coin, bool docked, double homeLeft, ScaleTransform dockScale)
        {
            coin.BeginAnimation(Canvas.LeftProperty, null);
            coin.BeginAnimation(Canvas.TopProperty, null);
            dockScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            dockScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            double scale = docked ? _zoomScale : 1.0;
            dockScale.ScaleX = scale;
            dockScale.ScaleY = scale;

            if (docked)
            {
                double left, top;
                GetDockPosition(coin, out left, out top);
                Canvas.SetLeft(coin, left);
                Canvas.SetTop(coin, top);
            }
            else
            {
                Canvas.SetLeft(coin, homeLeft);
                Canvas.SetTop(coin, CoinHomeY);
            }
        }

        #endregion

        #region Напитки

        private async void OnDrinkClick(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Border;
            Drink drink = _drinks.Find(d => d.Button == button);

            if (drink == null)
            {
                return;
            }

            e.Handled = true;
            await PourDrinkAsync(drink);
        }

        private async Task PourDrinkAsync(Drink drink)
        {
            if (drink == null || _busy || _glassFull || _credit < drink.Price)
            {
                return;
            }

            _busy = true;

            _credit -= drink.Price;
            _insertedCoins.Clear();
            _insertedCoins.AddRange(Decompose(_credit));
            UpdateState();

            // Зажигаем плашку.
            drink.NormalPlate.Visibility = Visibility.Collapsed;
            drink.LitPlate.Visibility = Visibility.Visible;
            drink.LitPlate.Opacity = 1;
            var pulse = new DoubleAnimation(1, 0.6, TimeSpan.FromMilliseconds(420))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            drink.LitPlate.BeginAnimation(OpacityProperty, pulse);

            // Стакан пуст, начинаем налив.
            LiquidScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            LiquidScale.ScaleY = 0;
            LiquidRect.Fill = drink.LiquidBrush;

            PourStream.Fill = drink.StreamBrush;
            PourStream.Visibility = Visibility.Visible;
            var flow = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(160))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            PourStreamScale.BeginAnimation(ScaleTransform.ScaleYProperty, flow);

            var fill = new DoubleAnimation(0, 0.82, TimeSpan.FromMilliseconds(2100))
            {
                BeginTime = TimeSpan.FromMilliseconds(250),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            fill.Completed += delegate
            {
                LiquidScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                LiquidScale.ScaleY = 0.82;
            };
            LiquidScale.BeginAnimation(ScaleTransform.ScaleYProperty, fill);

            await Task.Delay(2400);

            PourStreamScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PourStreamScale.ScaleY = 1;
            PourStream.Visibility = Visibility.Collapsed;

            await Task.Delay(500);

            // Гасим плашку и возвращаем автомат в исходное состояние.
            drink.LitPlate.BeginAnimation(OpacityProperty, null);
            drink.LitPlate.Opacity = 1;
            drink.LitPlate.Visibility = Visibility.Collapsed;
            drink.NormalPlate.Visibility = Visibility.Visible;

            _glassFull = true;
            _busy = false;
            UpdateState();
        }

        /// <summary>
        /// Клик по налитому стакану: он приближается к зрителю, жидкость
        /// «выпивается», затем стакан возвращается пустым, а автомат отдаляется.
        /// </summary>
        private async void OnGlassClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (_busy)
            {
                return;
            }

            if (_glassFull)
            {
                await DrinkFromGlassAsync();
                return;
            }

            await RinseGlassAsync();
        }

        /// <summary>
        /// Пустой стакан: сдвигается вправо, переворачивается и «промывается»
        /// фонтаном снизу, затем возвращается на место. Камера не меняется.
        /// </summary>
        private async Task RinseGlassAsync()
        {
            // Промывка доступна в любой момент, когда стакан пуст и автомат не занят.
            if (_busy || _glassFull)
            {
                return;
            }

            _busy = true;
            UpdateState();

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Сдвигаем вправо и переворачиваем вверх дном.
            GlassTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, GlassRinseX, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
            GlassRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease });

            await Task.Delay(420);

            // Промывка: фонтан снизу, лёгкий голубой оттенок и покачивание стакана.
            RinseFountain.Visibility = Visibility.Visible;

            var mainJet = new DoubleAnimation(0.45, 1.05, TimeSpan.FromMilliseconds(170))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            RinseMainJetScale.BeginAnimation(ScaleTransform.ScaleYProperty, mainJet);

            var leftJet = new DoubleAnimation(0.40, 0.95, TimeSpan.FromMilliseconds(210))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            RinseLeftJetScale.BeginAnimation(ScaleTransform.ScaleYProperty, leftJet);

            var rightJet = new DoubleAnimation(0.40, 0.95, TimeSpan.FromMilliseconds(190))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            RinseRightJetScale.BeginAnimation(ScaleTransform.ScaleYProperty, rightJet);

            var tint = new DoubleAnimation(0, 0.45, TimeSpan.FromMilliseconds(250))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            RinseTint.BeginAnimation(OpacityProperty, tint);

            var shake = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(700)
            };
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(GlassRinseX, TimeSpan.Zero));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(GlassRinseX + 4, TimeSpan.FromMilliseconds(120)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(GlassRinseX - 3, TimeSpan.FromMilliseconds(260)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(GlassRinseX + 3, TimeSpan.FromMilliseconds(400)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(GlassRinseX, TimeSpan.FromMilliseconds(560)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(GlassRinseX, TimeSpan.FromMilliseconds(700)));
            GlassTranslate.BeginAnimation(TranslateTransform.XProperty, shake);

            await Task.Delay(1000);

            RinseMainJetScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RinseMainJetScale.ScaleY = 1;
            RinseLeftJetScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RinseLeftJetScale.ScaleY = 1;
            RinseRightJetScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RinseRightJetScale.ScaleY = 1;
            RinseFountain.Visibility = Visibility.Collapsed;

            RinseTint.BeginAnimation(OpacityProperty, null);
            RinseTint.Opacity = 0;

            GlassTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            GlassTranslate.X = GlassRinseX;

            // Обратный переворот, затем возврат на место.
            GlassRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(180, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease });

            await Task.Delay(420);

            GlassTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(GlassRinseX, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });

            await Task.Delay(400);

            GlassTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            GlassRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            GlassTranslate.X = 0;
            GlassRotate.Angle = 0;

            _busy = false;
            UpdateState();
        }

        private async Task DrinkFromGlassAsync()
        {
            if (_busy || !_glassFull)
            {
                return;
            }

            _busy = true;
            UpdateState();

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var bringDuration = TimeSpan.FromMilliseconds(500);

            GlassScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, GlassForwardScale, bringDuration) { EasingFunction = ease });
            GlassScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, GlassForwardScale, bringDuration) { EasingFunction = ease });
            GlassTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, GlassForwardX, bringDuration) { EasingFunction = ease });
            GlassTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, GlassForwardY, bringDuration) { EasingFunction = ease });

            await Task.Delay(560);

            // Пьём глотками: уровень жидкости ступенчато падает.
            var drink = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(1700)
            };
            drink.KeyFrames.Add(new LinearDoubleKeyFrame(0.82, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.70,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.66,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.52,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.48,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(830)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.30,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.26,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1280)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            drink.KeyFrames.Add(new EasingDoubleKeyFrame(0.0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1650)), new SineEase { EasingMode = EasingMode.EaseIn }));

            LiquidScale.BeginAnimation(ScaleTransform.ScaleYProperty, drink);

            await Task.Delay(1760);

            LiquidScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            LiquidScale.ScaleY = 0;
            _glassFull = false;

            // Пустой стакан возвращается на место.
            var backDuration = TimeSpan.FromMilliseconds(450);

            GlassScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(GlassForwardScale, 1, backDuration) { EasingFunction = ease });
            GlassScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(GlassForwardScale, 1, backDuration) { EasingFunction = ease });
            GlassTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(GlassForwardX, 0, backDuration) { EasingFunction = ease });
            GlassTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(GlassForwardY, 0, backDuration) { EasingFunction = ease });

            await Task.Delay(500);

            GlassScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            GlassScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            GlassTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            GlassTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            GlassScale.ScaleX = 1;
            GlassScale.ScaleY = 1;
            GlassTranslate.X = 0;
            GlassTranslate.Y = 0;

            // После выпивания автомат отдаляется.
            await ZoomOutAsync();

            _busy = false;
            UpdateState();
        }

        #endregion
    }
}
