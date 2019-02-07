using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfAudioTool.Utilities;

namespace WpfAudioTool.Controls
{
    /// <summary>
    /// Interaction logic for SoundChart.xaml
    /// </summary>
    public partial class SoundChart : UserControl
    {
        List<float> samples;

        public SoundChart()
        {
            InitializeComponent();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (this.samples != null)
            {
                ShowSamples(this.samples);
            }
        }

        public void ShowSamples(List<float> samples)
        {
            this.samples = samples;
            DrawingCanvas.Children.Clear();
            if (samples.Count > 0)
            {
                List<float> maxPath = Numeric.Summarize(samples, (int)this.ActualWidth, (a, b) => { return (float)Math.Max(a, b); });
                List<float> minPath = Numeric.Summarize(samples, (int)this.ActualWidth, (a, b) => { return (float)Math.Min(a, b); });
                AddPath(maxPath, minPath);
                DrawingCanvas.InvalidateArrange();
            }
        }

        private void AddPath(List<float> upper, List<float> lower)
        {
            double scale = this.DrawingGrid.ActualHeight / 4;
            Point start = new Point(0, (upper[0] * scale) + scale);
            PathFigure fig = new PathFigure() { StartPoint = start };
            fig.IsClosed = true;
            fig.IsFilled = true;
            foreach (PathSegment s in (from i in Range(1, upper.Count) select new LineSegment(new Point(i, (upper[i] * scale) + scale), true)))
            {
                fig.Segments.Add(s);
            }
            foreach (PathSegment s in (from i in ReverseRange(1, upper.Count) select new LineSegment(new Point(i, (lower[i] * scale) + scale), true)))
            {
                fig.Segments.Add(s);
            }
            PathGeometry g = new PathGeometry();
            g.Figures.Add(fig);

            Path path = new Path()
            {
                Stroke = Brushes.AntiqueWhite,
                StrokeThickness = 1,
                Fill = Brushes.AntiqueWhite,
                Data = g
            };
            DrawingCanvas.Children.Add(path);
        }

        IEnumerable<int> Range(int min, int max)
        {
            for (int i = min; i < max; i++)
            {
                yield return i;
            }
        }
        IEnumerable<int> ReverseRange(int min, int max)
        {
            for (int i = max - 1; i >= min; i--)
            {
                yield return i;
            }
        }

        internal void Clear()
        {
            DrawingCanvas.Children.Clear();
        }
    }
}
