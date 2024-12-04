// See https://aka.ms/new-console-template for more information
using Laser_Converter;

namespace Converter_for_laser
{
    class Program {

        static void Main() {

            LaserConverter Laser = new LaserConverter();
            List<string> path = File.ReadAllLines(@"путь\к файлу\input.gcode").ToList();
            
            var result = Laser.Convert(path);
            
            if (result != null)
            {
                // Указываем путь к файлу для записи
                string outputFilePath = @"путь\к файлу\output.txt";
                
                // Записываем все строки в файл
                File.WriteAllLines(outputFilePath, result);
            }
            else
            {
                Console.WriteLine("Ошибка: результат преобразования пуст.");
            }
            
            Console.ReadKey();


            }
        }

}

           
   