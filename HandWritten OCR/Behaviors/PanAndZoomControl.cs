using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HandWritten_OCR.Behaviors
{
    public class PanAndZoomControl : Border
    {

        private UIElement child = null;
        private Point origin;
        private Point start;

        public enum ViewerSideModel
        {
            Left,
            Right
        }


        #region Dependency Properties

        public static readonly DependencyProperty ViewerSideProperty =
            DependencyProperty.Register(
                nameof(ViewerSide),
                typeof(ViewerSideModel),
                typeof(PanAndZoomControl),
                new PropertyMetadata(ViewerSideModel.Left));

        public ViewerSideModel ViewerSide
        {
            get => (ViewerSideModel)GetValue(ViewerSideProperty);
            set => SetValue(ViewerSideProperty, value);
        }

        public static readonly DependencyProperty ViewerSideCommandProperty =
            DependencyProperty.Register(
                nameof(ViewerSideCommand),
                typeof(ICommand),
                typeof(PanAndZoomControl));

        public ICommand ViewerSideCommand
        {
            get => (ICommand)GetValue(ViewerSideCommandProperty);
            set => SetValue(ViewerSideCommandProperty, value);
        }

        public static readonly DependencyProperty RotationProperty =
            DependencyProperty.Register(
                nameof(Rotation),
                typeof(double),
                typeof(PanAndZoomControl),
                new PropertyMetadata(0.0, OnRotationChanged));

        public double Rotation
        {
            get => (double)GetValue(RotationProperty);
            set => SetValue(RotationProperty, value);
        }

        private static void OnRotationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PanAndZoomControl)d;
            if (control.child == null) return;

            RotateTransform rt = control.GetRotateTransform(control.child);
            rt.Angle = (double)e.NewValue;
            rt.CenterX = control.ActualWidth / 2;
            rt.CenterY = control.ActualHeight / 2;
        }

        #endregion


        #region Transform Helpers

        private TranslateTransform GetTranslateTransform(UIElement element)
        {
            return (TranslateTransform)((TransformGroup)element.RenderTransform).Children.First(tr => tr is TranslateTransform);
        }

        private ScaleTransform GetScaleTransform(UIElement element)
        {
            return (ScaleTransform)((TransformGroup)element.RenderTransform).Children.First(tr => tr is ScaleTransform);
        }

        private RotateTransform GetRotateTransform(UIElement element)
        {
            return (RotateTransform)((TransformGroup)element.RenderTransform).Children.First(tr => tr is RotateTransform);
        }

        #endregion


        public override UIElement Child
        {
            get { return base.Child; }
            set
            {
                if (value != null && value != this.Child)
                    this.Initialize(value);
                base.Child = value;
            }
        }

        public void Initialize(UIElement element)
        {
            this.child = element;
            if (child != null)
            {
                TransformGroup group = new TransformGroup();
                ScaleTransform st = new ScaleTransform();
                RotateTransform rt = new RotateTransform();
                TranslateTransform tt = new TranslateTransform();
                group.Children.Add(st);
                group.Children.Add(rt);
                group.Children.Add(tt);
                child.RenderTransform = group;
                child.RenderTransformOrigin = new Point(0.0, 0.0);

                this.MouseWheel += Child_MouseWheel;
                this.MouseLeftButtonDown += Child_MouseLeftButtonDown;
                this.MouseLeftButtonUp += Child_MouseLeftButtonUp;
                this.MouseMove += Child_MouseMove;
                this.PreviewMouseRightButtonDown += new MouseButtonEventHandler(Child_PreviewMouseRightButtonDown);
            }
        }

        public void Reset()
        {
            if (child != null)
            {
                ScaleTransform st = GetScaleTransform(child);
                st.ScaleX = 1;
                st.ScaleY = 1;

                RotateTransform rt = GetRotateTransform(child);
                rt.Angle = 0;
                rt.CenterX = this.ActualWidth / 2;
                rt.CenterY = this.ActualHeight / 2;

                TranslateTransform tt = GetTranslateTransform(child);
                tt.X = 0;
                tt.Y = 0;

                // Sync the dependency property back to 0
                Rotation = 0;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (child == null) return;

            RotateTransform rt = GetRotateTransform(child);
            rt.CenterX = sizeInfo.NewSize.Width / 2;
            rt.CenterY = sizeInfo.NewSize.Height / 2;
        }


        #region Mouse Events

        private void Child_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (child != null)
            {
                ScaleTransform st = GetScaleTransform(child);
                TranslateTransform tt = GetTranslateTransform(child);

                double zoom = e.Delta > 0 ? .2 : -.2;
                if (!(e.Delta > 0) && (st.ScaleX < .4 || st.ScaleY < .4))
                    return;

                Point relative = e.GetPosition(child);
                double absoluteX = (relative.X * st.ScaleX) + tt.X;
                double absoluteY = (relative.Y * st.ScaleY) + tt.Y;

                st.ScaleX += zoom;
                st.ScaleY += zoom;

                tt.X = absoluteX - (relative.X * st.ScaleX);
                tt.Y = absoluteY - (relative.Y * st.ScaleY);
            }
        }

        private void Child_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (child != null)
            {
                Point mousePos = e.GetPosition(this);

                if (mousePos.X < 0 || mousePos.X > this.ActualWidth)
                    return;

                ViewerSide = mousePos.X < this.ActualWidth / 2
                    ? ViewerSideModel.Left
                    : ViewerSideModel.Right;

                ViewerSideCommand?.Execute(ViewerSide);

                TranslateTransform tt = GetTranslateTransform(child);
                start = e.GetPosition(this);
                origin = new Point(tt.X, tt.Y);
                this.Cursor = Cursors.Hand;
                child.CaptureMouse();
            }
        }

        private void Child_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (child != null)
            {
                child.ReleaseMouseCapture();
                this.Cursor = Cursors.Arrow;
            }
        }

        private void Child_MouseMove(object sender, MouseEventArgs e)
        {
            if (child != null && child.IsMouseCaptured)
            {
                TranslateTransform tt = GetTranslateTransform(child);
                Vector v = start - e.GetPosition(this);
                tt.X = origin.X - v.X;
                tt.Y = origin.Y - v.Y;
            }
        }

        private void Child_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.Reset();
        }

        #endregion

    }
}