using Laser_Converter_Commands;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Laser_Converter{
    public sealed class LaserConverter{

        private const float MAX_STEP = 2.5f;

        private readonly Regex cmdPattern = new Regex(@"(?<command>[a-zA-Z]+)(?<number>[-+]?(?:\d*\.\d+|\d+))?");

        public LaserConverter(){
            //инициализация
        }

        public List<string> Convert(List<string> Gcodes){
            List<Command> buffer = ParseLayer(Gcodes);

            buffer = CompressLayer(buffer);
            buffer = SplitLayer(buffer, MAX_STEP);

            List<string> result = new List<string>();
            foreach(Command line in buffer){
                result.AddRange(line.GetCommandStrings(true));
            }

            return result;

        }

        //Прайват методы
        private List<Command> ParseLayer(List<string> Gcodes){

            List<Command> buffer = new List<Command> { new TriggerLevel(2, 1), new Wait(1000) };
            List<Dictionary<string, object>> partsInfo = new List<Dictionary<string, object>>();
            List<float[]> currentContour = new List<float[]>();
            bool appendContours = false;
            Dictionary<string, object>? partDict = null;

            foreach (string line in Gcodes){
                
                var (parsedCmds, parsedMetadata) = ParseLine(line);
                try{
                    ;
                } catch {
                    continue;
                }

                if (parsedCmds != null && parsedCmds.Count > 0)
                {
                    buffer.AddRange(parsedCmds);

                    if (appendContours && parsedCmds[0] is Mark mark)
                        currentContour.Add(mark.Point.ToArray());
                }

                switch (parsedMetadata)
                {
                    case ObjMetadata objMetadata:

                        if (partDict != null)
                        {
                            if (currentContour.Count > 0)
                                partDict.Add("contours",currentContour);
                        
                            partsInfo.Add(partDict);

                            buffer.AddRange([new TriggerLevel(4, 0), new Wait(1)]);
                        }

                        partDict = new Dictionary<string, object>
                        {
                            { "model_name", objMetadata.ModelName },
                            { "part_number", objMetadata.PartNumber },
                            { "contours", new List<List<float[]>>() }
                        };
                        currentContour = new List<float[]>();

                        if (appendContours)
                        {
                            buffer.AddRange([new TriggerLevel(5, 0), new Wait(1)]);
                        }
                        appendContours = false;

                        buffer.AddRange([new TriggerLevel(4, 1)]);

                        break;

                    case ContourMetadata contourMetadata:
                    {
                        switch (contourMetadata.State)
                        {
                            case "start":
                                if (contourMetadata.IsExternal)
                                {
                                    if (partDict != null)
                                    {
                                        if (currentContour.Count > 0)
                                        {
                                            partDict.Add("contours",currentContour);
                                        }
                                    }
                                    
                                    currentContour = new List<float[]>();
                                    appendContours = true;
                                    buffer.AddRange([new TriggerLevel(5, 1),new Wait(1)]);
                                
                                }
                                break;

                            case "stop":
                                if (contourMetadata.IsExternal)
                                {
                                    if (appendContours)
                                    {
                                        buffer.AddRange([new TriggerLevel(5, 0),new Wait(1)]);
                                    }
                                    appendContours = false;
                                }
                                break;

                            default:
                                throw new Exception($"Ошибка парсинга контура в слое");

                        }
                        break;
                    }
                }
            }

            if (partDict != null)
            {
                if (currentContour.Count > 0)
                    partDict.Add("contours",currentContour);
    
                if (appendContours)
                {
                    buffer.AddRange([new TriggerLevel(5, 0),new Wait(1)]);
                }
                partsInfo.Add(partDict);
                buffer.AddRange([new TriggerLevel(4, 0),new Wait(1)]);
            }

            buffer.Add(new TriggerLevel(2, 0));

            return buffer;

        }

        private (List<Command> parsedCmd, object? parsedMetadata) ParseLine(string line){

            line = line.Trim();

            if (line.Length == 0)
            {
                return (new List<Command>(), null); 
            }

            var splitted = line.Split(';');
            var commands = splitted[0];
            var comment = string.Join(';', splitted.Skip(1));
            List<Command> parsedCmd = new List<Command>(); 
            object? parsedComment = null; 

            if (commands.Length > 0)
            {
                var founds = commands.Split(" ").Select(cmd => cmdPattern.Match(cmd)).ToList();
            
                var command = founds[0].Value;

                switch (command)
                {
                    case "G1":

                        var parameters = founds.Skip(1).Select(m => m.Groups["command"].Value).ToArray();
                        string line_parameteres = string.Join(" ", parameters);

                        switch (line_parameteres)
                        {
                            case "X Y E":

                                parsedCmd.Add(new Mark([
                                float.Parse(founds[1].Groups["number"].Value,CultureInfo.InvariantCulture),
                                float.Parse(founds[2].Groups["number"].Value,CultureInfo.InvariantCulture)]));

                                break;
                            
                            case "X Y F":
                                
                                parsedCmd.Add(new Move([
                                float.Parse(founds[1].Groups["number"].Value,CultureInfo.InvariantCulture),
                                float.Parse(founds[2].Groups["number"].Value,CultureInfo.InvariantCulture)]));  
                                
                                break;

                            case "X Y":

                                parsedCmd.Add(new Move([
                                float.Parse(founds[1].Groups["number"].Value,CultureInfo.InvariantCulture),
                                float.Parse(founds[2].Groups["number"].Value,CultureInfo.InvariantCulture)]));

                                break;

                            case "F":
                                var speedValue = float.Parse(founds[1].Groups["number"].Value) / 60;
                                speedValue = (int)Math.Round(speedValue * 10_000);
                                int speed = (int)(speedValue % 10_000);
                                int power = (int)(speedValue / 10_000);
                                parsedCmd.AddRange([new Power(power), new MarkSpeed(speed) ]);
                                break;

                        }
                        break;

                    case "G81":
                    
                        switch (founds[1].Value)
                        {
                            case "START":
                                parsedCmd.Add(new TriggerPulse(1));
                                break;
                            case "STOP":
                                parsedCmd.Add(new TriggerPulse(3));
                                break;
                            default:
                                throw new Exception($"Wrong parameters for G81: {founds[1].Value}");
                        }
                        break;

                    case "G4":
                        switch (founds[1].Value[0])
                        {
                            case 'P':
                                var waitValue = int.Parse(founds[1].Groups["number"].Value);
                                parsedCmd.Add(new Wait(waitValue * 100));
                                break;
                            default:
                                throw new Exception($"Wrong parameters for G4: {founds[1].Value}");
                        }
                        break;
                }
            }

            comment = comment.Trim();
            if (comment.StartsWith("start_OBJ_NAME"))
            {
                var values = comment.Split().Select(x => x.Split('=')[1]).ToArray();
                parsedComment = new ObjMetadata(values[0], int.Parse(values[1]));
            }
            else if (comment == "TYPE:External perimeter")
            {
                parsedComment = new ContourMetadata("start");
            }
            else if (comment.StartsWith("TYPE:"))
            {
                parsedComment = new ContourMetadata("stop");
            }

            return (parsedCmd, parsedComment);

        }

        private List<Command> CompressLayer(List<Command> layer){

            float? power = null, markSpeed = null;
            float[]? curPoint = null;
            List<Command> newLayer = new List<Command>();
            List<float[]> points = new List<float[]>();

            foreach (var cmd in layer)
            {
                switch (cmd)
                {
                    case Power powerCmd:
                        if (powerCmd.Value != power)
                        {
                            points = AddPoints(newLayer, points);
                            newLayer.Add(cmd);
                            power = powerCmd.Value;
                        }
                        break;
                    case MarkSpeed markSpeedCmd:
                        if (markSpeedCmd.Value != markSpeed)
                        {
                            points = AddPoints(newLayer, points);
                            newLayer.Add(cmd);
                            markSpeed = markSpeedCmd.Value;
                        }
                        break;
                    case Mark markCmd:
                        if (points.Count == 0)
                        {
                            if (curPoint == null)
                            {
                                throw new InvalidOperationException("Mark before first Move!");
                            }
                            points = new List<float[]> { curPoint };
                        }

                        points.Add(markCmd.Point);
                        curPoint = markCmd.Point;
                        break;
                    case Move moveCmd:
                        points = AddPoints(newLayer, points);
                        points.Add(moveCmd.Points);
                        curPoint = moveCmd.Points;
                        break;
                    default:
                        points = AddPoints(newLayer, points);
                        newLayer.Add(cmd);
                        break;
                }
            }

            if (points.Count > 0)
            {
                AddPoints(newLayer, points);
            }

            return newLayer.Count > 0 ? newLayer : null;
        }

        private List<float[]> AddPoints(List<Command> newLayer, List<float[]> points)
        {
            if (points.Count > 0)
            {
                int count = points.Count;
                switch (count)
                {
                    case 0:
                        break;                       
                    case 1:
                        newLayer.Add(new Move(points[0]));
                        break;

                    case 2:
                        newLayer.Add(new Line(new Move(points[0]), new Mark(points[1]),null));
                        break;

                    default:
                        var ptOld = points[0];
                        var lines = new List<Line>();

                        for (int i = 1; i < count; i++)
                        {
                            var ptNew = points[i];
                            lines.Add(new Line(new Move(ptOld), new Mark(ptNew),null));
                            ptOld = ptNew;
                        }

                        newLayer.Add(new Polyline(lines));
                        break;
                }

                return new List<float[]>();
            }

            return points;
        }

        private  List<Command> SplitLayer(List<Command> layer, float maxStep)
        {
            List<Command> newLayer = new List<Command>();

            foreach (Command cmd in layer)
            {
                if (cmd is Polyline)
                {
                    newLayer.Add((cmd as Polyline).SplitOnSubPts(maxStep));
                }
                else if (cmd is Line)
                {
                    newLayer.Add((cmd as Line).SplitOnSubPts(maxStep));
                }
                else
                {
                    newLayer.Add(cmd);
                }
            }

            return newLayer;
        }

    
    }

}