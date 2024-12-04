using System.ComponentModel;
using System.Numerics;

namespace Laser_Converter_Commands
{
    public static class GcodeUtils
    {
        public static float FindAngle(float[] a, float[] b, float[] c)
        {
            Vector2 aVector = new Vector2(a[0],a[1]);
            Vector2 bVector = new Vector2(b[0],b[1]);
            Vector2 cVector = new Vector2(c[0],c[1]);

            var baVector = aVector - bVector; 
            var bcVector = cVector - bVector;

            float cosineAngle = Vector2.Dot(baVector, bcVector) / (Vector2.Distance(aVector, bVector) * Vector2.Distance(bVector, cVector));
            if (Math.Abs(cosineAngle) > 1)
            {
                cosineAngle = (float)Math.Round(cosineAngle, 4);
            }

            return (float)Math.Acos(-cosineAngle);
        }

        public static List<string> CombineCommands(List<Command> commands, bool addDelay = false)
        {
            List<string> cmds = new List<string>{};
            foreach(var command in commands){
                cmds.AddRange(command.GetCommandStrings(addDelay));
            }

            return cmds;
        }

    }


    public abstract class Command
    {
        public abstract List<string> GetCommandStrings(bool addDelay = false);

    }


    public class Move : Command
    {
        public float[] Points { get; }
        public string Opcode { get; }
        public string DELAY_OPCODE { get; }

        public Move(float[] points)
        {
            Points = points;
            DELAY_OPCODE = "J_D";
            Opcode = "G0";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            var cmds = new List<string> { $"{Opcode} {Points[0]} {Points[1]}" };
            if (addDelay)
            {
                cmds.Add(DELAY_OPCODE);
            }
            return cmds;
        }

    }

    public class Mark : Command
    {
        public float[] Point { get; }
        public string Opcode { get; }
        public string DELAY_OPCODE { get; }

        public Mark(float[] point)
        {
            Point = point;
            DELAY_OPCODE = "M_D";
            Opcode = "G1";

        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            var cmds = new List<string> { $"{Opcode} {Point[0]} {Point[1]}" };
            if (addDelay)
            {
                cmds.Add(DELAY_OPCODE);
            }
            return cmds;
        }

    }

    public class Line : Command
    {
        public Move StartPt { get; }
        public Mark StopPt { get; }
        public List<Mark> SubPts { get; }

        public Line(Move startPt, Mark stopPt, List<Mark> subPts)
        {
            StartPt = startPt;
            StopPt = stopPt;
            SubPts = subPts;
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            List<string> test =
            [
                .. StartPt.GetCommandStrings(addDelay),
                .. SubPts.SelectMany(X => X.GetCommandStrings(addDelay)),
                .. StopPt.GetCommandStrings(addDelay),
            ];
            return test;
        }
        
        public Line SplitOnSubPts(float maxStep)
        {
            Vector2 start = new Vector2(StartPt.Points[0],StartPt.Points[1]);
            Vector2 end = new Vector2(StopPt.Point[0],StopPt.Point[1]);
            
            // Вычисляем количество точек
            int numOfPoints = (int)Math.Ceiling(Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)) / maxStep) + 1;
            
            var SubPts = new List<Mark>();
            // Создаем промежуточные точки с использованием линейной интерполяции
            for (int i = 1; i < numOfPoints - 1; i++)
            {
                float t = (float)i / (numOfPoints - 1);
                float x = start.X + t * (end.X - start.X);
                float y = start.Y + t * (end.Y - start.Y);
                SubPts.Add(new Mark(new float[] { x, y }));
            }

            // Возвращаем новый объект Line с начальной, конечной и промежуточными точками в формате float[]
            return new Line(new Move(new float[]{ start.X, start.Y }), new Mark(new float[]{ end.X, end.Y }), SubPts);
        }

    }
    

    public class Polyline : Command
    {
        public List<Line> Lines { get; }
        public string DELAY_OPCODE { get; }

        public Polyline(IEnumerable<Line> lines)
        {
            Lines = lines.ToList();
            DELAY_OPCODE = "P_D";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            if (Lines.Count == 0)
            {
                return new List<string>();
            }

            var cmds = new List<string>();
            cmds.AddRange(Lines[0].StartPt.GetCommandStrings(addDelay));

            float[] pt1 = Lines[0].StartPt.Points;
            float[] pt2 = Lines[0].StopPt.Point;
            
            for (int i = 0; i < Lines.Count - 1; i++)
            {
                cmds.AddRange(GcodeUtils.CombineCommands(Lines[i].SubPts.Cast<Command>().ToList()));
                cmds.AddRange(Lines[i].StopPt.GetCommandStrings());

                if (addDelay)
                {
                    float[] pt3 = Lines[i + 1].StopPt.Point;
                    float angle = GcodeUtils.FindAngle(pt1, pt2, pt3);
                    cmds.Add($"{DELAY_OPCODE} {angle:F2}");

                    pt1 = pt2;
                    pt2 = pt3;
                }
            }

            cmds.AddRange(GcodeUtils.CombineCommands(Lines[^1].SubPts.Cast<Command>().ToList()));
            cmds.AddRange(Lines[^1].StopPt.GetCommandStrings(addDelay));

            return cmds;
        }

        public Polyline SplitOnSubPts(float maxStep)
        {
            var splitLines = Lines.Select(line => line.SplitOnSubPts(maxStep)).ToList();
            return new Polyline(splitLines);
        }
    }

    public class Power : Command
    {
        public string Opcode { get; }
        public float Value { get; }

        public Power(float value)
        {
            Value = value;
            Opcode = "P_L";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            return new List<string> { $"{Opcode} {Value:F2}" };
        }

    }

    public class MarkSpeed : Command
    {
        public float Value { get; }
        public string Opcode { get; }

        public MarkSpeed(float value)
        {
            Value = value;
            Opcode = "S_L";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            return new List<string> { $"{Opcode} {Value:F2}" };
        }

    }

    public class Wait : Command
    {
        public int Value { get; }
        public string Opcode { get; }

        public Wait(int value)
        {
            Value = value;
            Opcode = "WAIT";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            return new List<string> { $"{Opcode} {Value}" };
        }

    }

    public class TriggerPulse : Command
    {
        public int TriggerNum { get; }
        public string Opcode { get; }

        public TriggerPulse(int triggerNum)
        {
            TriggerNum = triggerNum;
            Opcode = "T_P";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            return new List<string> { $"{Opcode} {TriggerNum}" };
        }
        
    }

    public class TriggerLevel : Command
    {
        public int TriggerNum { get; }
        public int Value { get; }
        public string Opcode { get; }

        public TriggerLevel(int triggerNum, int value)
        {
            TriggerNum = triggerNum;
            Value = value;
            Opcode = "T_L";
        }

        public override List<string> GetCommandStrings(bool addDelay = false)
        {
            return new List<string> { $"{Opcode} {TriggerNum} {Value}" };
        }

    }

    public class ContourMetadata
    {
        public string State { get; }
        public bool IsExternal { get; }

        public ContourMetadata(string state)
        {
            State = state;
            IsExternal = true;
        }
    }

    public class ObjMetadata
    {
        public string ModelName { get; set; }
        public int PartNumber { get; set; }

        public ObjMetadata(string modelName, int partNumber)
        {
            ModelName = modelName;
            PartNumber = partNumber;
        }
    }

}