using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SvgCleaner
{
    internal class Position
    {
        public double x { get; set; }
        public double y { get; set; }
    }

    /// <summary>
    /// Line segment between start en end position where the positions are absolute.
    /// The type of segment between the two point is defined using the command and the additional optional parameters 
    /// </summary>
    internal class Segment
    {
        public Position Begin { get; set; }
        public Position End { get; set; }

        public string Command { get; set; }

        public string Rx { get; set; }
        public string Ry { get; set; }
        public string Angle { get; set; }
        public string LargeArcFlag { get; set; }
        public string SweepFlag { get; set; }
    }

    internal class Shape
    {
        public Shape(XElement svgShape)
        {
            SvgShape = svgShape;
        }
        public Position Begin { get; set; }
        public Position End { get; set; }
        public XElement SvgShape { get; set; }
    }

    /// <summary>
    /// Optimize flattened SVG file created by FreeCAD for laser cutting with LaserGRBL,
    /// Break the image in the smallest segments. While ignoring the direction remove all duplicate segments.
    /// Recompose the image by joining all segments with the minimal distance between grouped segments.
    /// </summary>
    internal class Optimizer
    {
        private Position CurrentPosition;
        private readonly List<Segment> Segments = new List<Segment>();
        private List<Shape> Replacement = new List<Shape>();

        internal void Start(string file)
        {

            XDocument svg = XDocument.Parse(File.ReadAllText(file));

            //Split in smallest segments
            IEnumerable<XElement> childElements =
                from el in svg.Root.Elements(@"{http://www.w3.org/2000/svg}g").Elements()
                select el;

            foreach (XElement el in childElements)
            {
                if (el.Name.LocalName != "path")
                {
                    Shape s = new Shape(el);
                    Replacement.Add(s);

                    if (el.Name.LocalName == "circle")
                    {
                        s.Begin = new Position
                        {
                            x = Parse(el.Attribute("cx").Value),
                            y = Parse(el.Attribute("cy").Value)
                        };
                    }
                }

                //path
                foreach (XAttribute a in el.Attributes())
                {
                    if (a.Name == "d")
                    {
                        FindPath(a);
                    }
                }
            }

            //Join all segments
            CreatePaths();

            //Create new optimize SVG file
            XElement g = svg.Root.Elements(@"{http://www.w3.org/2000/svg}g").FirstOrDefault();
            g.ReplaceNodes(ReorderShapes(Replacement).Select(s => s.SvgShape));
            svg.Save(file.Replace(".svg", "new.svg"));
        }

        private List<Shape> ReorderShapes(List<Shape> lst)
        {
            // Find optimal start position (bottom-left)
            var newLst = lst.Where(s => s.Begin == null).ToList();

            lst = lst.Where(s => s.Begin != null).ToList();
            double x = lst.Min(s => s.Begin.x);
            double y = lst.Min(s => s.Begin.y);

            //Find shapes...
            while (lst.Count > 0)
            {
                var t = lst.Aggregate((curMin, s) => SmallestDistance(s, curMin, x, y));
                newLst.Add(t);
                x = t.End.x;
                y = t.End.y;
                lst.Remove(t);
            }
            return newLst;
        }

        private Shape SmallestDistance(Shape a, Shape b, double x, double y)
        {
            if (b == null)
            {
                return a;
            }
            double da = Distance(a, x, y);
            double db = Distance(b, x, y);

            if (da < db)
            {
                return a;
            }
            if (da == db)
            {
                return a.Begin.y < b.Begin.y ? a : b;
            }
            return b;
        }

        private double Distance(Shape a, double x, double y)
        {
            return (a.Begin.x - x) * (a.Begin.x - x) + (a.Begin.y - y) * (a.Begin.y - y);
        }

        private void FindPath(XAttribute a)
        {
            var m = new Regex("(?<command>[MmLIHhVvCcSsQqTtAaZz]{1}?)\\s+((?<p0>[+\\-.0-9]*)\\s)?((?<p1>[+\\-.0-9]*)\\s)?((?<p2>[+\\-.0-9]*)\\s)?((?<p3>[+\\-.0-9]*)\\s)?((?<p4>[+\\-.0-9]*)\\s)?((?<p5>[+\\-.0-9]*)\\s)?((?<p6>[+\\-.0-9]*))?").Matches(a.Value);

            if (m.Count > 0)
            {
                if (m[0].Groups["command"].Value != "M")
                {
                    // Add M-command using CurrentPosition to make standalone path commands
                }
                else
                {

                }
                Position Start = null;

                foreach (Match cmd in m)
                {
                    if (cmd.Groups["command"].Value == "Z")
                    {
                        // Remove Z-command by conversion to L with goto start position.
                        // For laser cutting the specific Z-behavior is not relevant and is ignored.
                        AddLine(CurrentPosition, Start, "L");
                        CurrentPosition = Start;
                        continue;
                    }

                    if (cmd.Groups["command"].Value == "A")
                    {
                        double x = Parse(cmd.Groups["p5"].Value);
                        double y = Parse(cmd.Groups["p6"].Value);
                        if (CurrentPosition != null)
                        {
                            AddEllipticalArc(CurrentPosition, new Position { x = x, y = y }, "A", cmd.Groups["p0"].Value, cmd.Groups["p1"].Value, cmd.Groups["p2"].Value, cmd.Groups["p3"].Value, cmd.Groups["p4"].Value);
                        }
                        CurrentPosition = new Position { x = x, y = y };

                    }

                    if (cmd.Groups["command"].Value == "M")
                    {
                        double x = Parse(cmd.Groups["p0"].Value);
                        double y = Parse(cmd.Groups["p1"].Value);
                        CurrentPosition = new Position { x = x, y = y };
                        Start = new Position { x = x, y = y };

                    }

                    if (cmd.Groups["command"].Value == "L")
                    {
                        double x = Parse(cmd.Groups["p0"].Value);
                        double y = Parse(cmd.Groups["p1"].Value);
                        if (CurrentPosition != null)
                        {
                            AddLine(CurrentPosition, new Position { x = x, y = y }, "L");
                        }
                        CurrentPosition = new Position { x = x, y = y };
                    }

                }
            }
        }

        private double Parse(string text)
        {
            return Math.Round(double.Parse(text, CultureInfo.InvariantCulture), 4);
        }

        #region SVG Elements
        // Add segments without duplicates and change direction from left (x-min) to right (x-max) or bottom (y-min) to top (y-max).

        private void AddLine(Position start, Position end, string command)
        {
            if (start.x > end.x || (start.x == end.x && start.y > end.y))
            {
                (start.x, start.y, end.x, end.y) = (end.x, end.y, start.x, start.y);
            }
            if (start.x == end.x && start.y == end.y)
            {
                return;
            }

            if (Segments.Count(l => l.Begin.x == start.x && l.Begin.y == start.y && l.End.x == end.x && l.End.y == end.y) == 0)
            {
                Segments.Add(new Segment { Begin = start, End = end, Command = command });
            }
        }

        private void AddEllipticalArc(Position start, Position end, string command, string rx, string ry, string angle, string largeArcFlag, string sweepFlag)
        {
            if (start.x > end.x || (start.x == end.x && start.y > end.y))
            {
                (start.x, start.y, end.x, end.y) = (end.x, end.y, start.x, start.y);
                sweepFlag = sweepFlag == "1" ? "0" : "1";
            }
            if (start.x == end.x && start.y == end.y)
            {
                return;
            }

            if (Segments.Count(l => l.Begin.x == start.x && l.Begin.y == start.y && l.End.x == end.x && l.End.y == end.y) == 0)
            {
                Segments.Add(new Segment { Begin = start, End = end, Command = command, Rx = rx, Ry = ry, Angle = angle, LargeArcFlag = largeArcFlag, SweepFlag = sweepFlag });
            }
        }

        #endregion

        private void CreatePaths()
        {
            List<List<Segment>> paths = new List<List<Segment>>();
            do
            {
                //Start, find segment with start position closed to the origin (bottom-left)
                List<Segment> path = new List<Segment>();
                paths.Add(path);

                double x = Segments.Min(s => s.Begin.x);
                double y = Segments.Min(s => s.Begin.y);

                Segment line = Segments.Aggregate((curMin, s) => curMin == null || (s.Begin.x - x) * (s.Begin.x - x) + (s.Begin.y - y) * (s.Begin.y - y) <
(curMin.Begin.x - x) * (curMin.Begin.x - x) + (curMin.Begin.y - y) * (curMin.Begin.y - y) ? s : curMin);

                path.Add(line);
                Segments.Remove(line);

                int count;
                do
                {
                    count = path.Count;
                    Segment nextLine;

                    // After
                    nextLine = GetNextLineAfter(line);
                    while (nextLine != null)
                    {
                        path.Add(nextLine);
                        Segments.Remove(nextLine);
                        line = nextLine;
                        nextLine = GetNextLineAfter(line);
                    }

                    //Before
                    nextLine = GetNextLineBefore(line);
                    while (nextLine != null)
                    {
                        path.Insert(0, nextLine);
                        Segments.Remove(nextLine);
                        line = nextLine;
                        nextLine = GetNextLineBefore(line);
                    }

                } while (count != path.Count);

            } while (Segments.Count > 0);

            foreach (var p in paths)
            {
                XElement e = new XElement(@"{http://www.w3.org/2000/svg}path",
                    new XAttribute("d", CreateAttribute(p)),
                    new XAttribute("stroke", "#000000"),
                    new XAttribute("stroke-width", "0.35 px"),
                    new XAttribute("style", "stroke-width:0.35;fill:#ffffff;fill-opacity:1.0;")
                    );
                Replacement.Add(new Shape(e) { Begin = p[0].Begin, End = p[p.Count - 1].End });
            }
        }

        private Segment GetNextLineAfter(Segment line)
        {
            Segment nextLine = Segments.Where(l => line.End.x == l.Begin.x && line.End.y == l.Begin.y).FirstOrDefault();
            if (nextLine == null)
            {
                nextLine = Segments.Where(l => line.End.x == l.End.x && line.End.y == l.End.y).FirstOrDefault();
                if (nextLine != null)
                {
                    Segments.Remove(nextLine);
                    (nextLine.Begin.x, nextLine.Begin.y, nextLine.End.x, nextLine.End.y) = (nextLine.End.x, nextLine.End.y, nextLine.Begin.x, nextLine.Begin.y);
                    if (nextLine.SweepFlag != null)
                    {
                        nextLine.SweepFlag = nextLine.SweepFlag == "1" ? "0" : "1";
                    }
                }
            }
            return nextLine;
        }

        private Segment GetNextLineBefore(Segment line)
        {
            Segment nextLine = Segments.Where(l => line.Begin.x == l.Begin.x && line.Begin.y == l.Begin.y).FirstOrDefault();
            if (nextLine == null)
            {
                nextLine = Segments.Where(l => line.Begin.x == l.End.x && line.Begin.y == l.End.y).FirstOrDefault();
                if (nextLine != null)
                {
                    Segments.Remove(nextLine);
                    (nextLine.Begin.x, nextLine.Begin.y, nextLine.End.x, nextLine.End.y) = (nextLine.End.x, nextLine.End.y, nextLine.Begin.x, nextLine.Begin.y);
                    if (nextLine.SweepFlag != null)
                    {
                        nextLine.SweepFlag = nextLine.SweepFlag == "1" ? "0" : "1";
                    }
                }
            }
            return nextLine;
        }

        private string CreateAttribute(List<Segment> p)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"M {p[0].Begin.x.ToString(CultureInfo.InvariantCulture)} {p[0].Begin.y.ToString(CultureInfo.InvariantCulture)} ");
            foreach (Segment l in p)
            {
                if (l.Command == "L")
                {
                    sb.Append($"L {l.End.x.ToString(CultureInfo.InvariantCulture)} {l.End.y.ToString(CultureInfo.InvariantCulture)} ");
                }
                else if (l.Command == "A")
                {
                    sb.Append($"A {l.Rx} {l.Ry} {l.Angle} {l.LargeArcFlag} {l.SweepFlag} {l.End.x.ToString(CultureInfo.InvariantCulture)} {l.End.y.ToString(CultureInfo.InvariantCulture)} ");
                }
            }
            return sb.ToString();
        }
    }
}



