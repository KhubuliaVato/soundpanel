using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SoundPanel;

public partial class NameDialog : Window
{
    public NameDialog()
    {
        InitializeComponent();
    }

    public string GroupName => NameBox.Text;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        NameBox.Focus();
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!NameBox.IsMouseOver && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
