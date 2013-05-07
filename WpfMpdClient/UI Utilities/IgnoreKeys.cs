using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI
{
  using System.Windows;
  using System.Windows.Input;

  public static class IgnoreKeys
  {
    public static void SetKey(DependencyObject depObj, Key value)
    {
      depObj.SetValue(KeyProperty, value);
    }

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(Key),
        typeof(IgnoreKeys),
        new FrameworkPropertyMetadata(Key.None, OnKeySet));

    static void OnKeySet(DependencyObject depObj, DependencyPropertyChangedEventArgs args)
    {
      var key = (Key)args.NewValue;
      var uiElement = depObj as UIElement;
      uiElement.PreviewKeyDown +=
        (object _, System.Windows.Input.KeyEventArgs e) => {
          if (key != Key.None && e.Key == key &&
              (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
          {
            var parent = LogicalTreeHelper.GetParent(depObj) as UIElement;
            if (parent != null)
            {
              parent.RaiseEvent(new KeyEventArgs(
                  Keyboard.PrimaryDevice,
                  PresentationSource.FromDependencyObject(parent),
                  0,
                  key) { RoutedEvent = Keyboard.KeyDownEvent }
              );
              e.Handled = true;
            }
          }
        };
    }
  }
}