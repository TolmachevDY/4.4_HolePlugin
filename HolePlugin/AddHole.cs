using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class AddHole : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //подключаемся к файлам
            Document arDoc = commandData.Application.ActiveUIDocument.Document;
            Document ovDoc = arDoc.Application.Documents.OfType<Document>().Where(x => x.Title.Contains("ОВ")).FirstOrDefault();
            if(ovDoc==null)
            {
                TaskDialog.Show("Ошибка", "Файл ОВ не найден");
                return Result.Cancelled;
            }
                        
            FamilySymbol familySymbol = new FilteredElementCollector(arDoc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .OfType<FamilySymbol>()
                .Where(x => x.FamilyName.Equals("Отверстие"))
                .FirstOrDefault();
            if(familySymbol==null)
            {
                TaskDialog.Show("Ошибка", "Семейство \"Отверстие\" не найдено");
                return Result.Cancelled;
            }

            //находим все воздуховоды
            List<Duct> ducts = new FilteredElementCollector(ovDoc)
                .OfClass(typeof(Duct))
                .OfType<Duct>()
                .ToList();

            //находим все трубы
            List<Pipe> pipes = new FilteredElementCollector(ovDoc)
                .OfClass(typeof(Pipe))
                .OfType<Pipe>()
                .ToList();


            //находим 3D вид
            View3D view3D = new FilteredElementCollector(arDoc)
                .OfClass(typeof(View3D))
                .OfType<View3D>()
                .Where(x => !x.IsTemplate)
                .FirstOrDefault();
            if (view3D == null)
            {
                TaskDialog.Show("Ошибка", "Не найден 3D вид");
                return Result.Cancelled;
            }
            //находим точки пересечения воздуховодов со стенами
            ReferenceIntersector referenceIntersector = new ReferenceIntersector(new ElementClassFilter(typeof(Wall)), FindReferenceTarget.Element, view3D);

            Transaction transaction = new Transaction(arDoc);
            transaction.Start("Создание отверстий");
            foreach (Duct d in ducts)
            {
                Line curve = (d.Location as LocationCurve).Curve as Line;
                XYZ point = curve.GetEndPoint(0);
                XYZ direction = curve.Direction;

                List<ReferenceWithContext> intersections = referenceIntersector.Find(point, direction)
                    .Where(x => x.Proximity <= curve.Length)
                    .Distinct(new ReferenceWithContextElementEqualityComparer())
                    .ToList();
                foreach (ReferenceWithContext refer in intersections)
                {
                    double proximity = refer.Proximity;
                    Reference reference = refer.GetReference();
                    Wall wall = arDoc.GetElement(reference.ElementId) as Wall;
                    Level level = arDoc.GetElement(wall.LevelId) as Level;
                    XYZ pointHole = point + (direction * proximity);

                    //создаем отверстия
                    FamilyInstance hole = arDoc.Create.NewFamilyInstance(pointHole, familySymbol, wall, level, StructuralType.NonStructural);
                    //задаем параметры создаваемого отверстия
                    Parameter width = hole.LookupParameter("Ширина");
                    Parameter height = hole.LookupParameter("Высота");
                    width.Set(d.Diameter);
                    height.Set(d.Diameter);
                }
            }
            //создаем отверстия для труб
            foreach (Pipe p in pipes)
            {
                Line curve = (p.Location as LocationCurve).Curve as Line;
                XYZ point = curve.GetEndPoint(0);
                XYZ direction = curve.Direction;

                List<ReferenceWithContext> intersections = referenceIntersector.Find(point, direction)
                    .Where(x => x.Proximity <= curve.Length)
                    .Distinct(new ReferenceWithContextElementEqualityComparer())
                    .ToList();
                foreach (ReferenceWithContext refer in intersections)
                {
                    double proximity = refer.Proximity;
                    Reference reference = refer.GetReference();
                    Wall wall = arDoc.GetElement(reference.ElementId) as Wall;
                    Level level = arDoc.GetElement(wall.LevelId) as Level;
                    XYZ pointHole = point + (direction * proximity);

                    //создаем отверстия
                    FamilyInstance hole = arDoc.Create.NewFamilyInstance(pointHole, familySymbol, wall, level, StructuralType.NonStructural);
                    //задаем параметры создаваемого отверстия
                    Parameter width = hole.LookupParameter("Ширина");
                    Parameter height = hole.LookupParameter("Высота");
                    width.Set(p.Diameter);
                    height.Set(p.Diameter);
                }
            }
            transaction.Commit();
            return Result.Succeeded;
        }
    }
    //проверка найденых пересечений
    public class ReferenceWithContextElementEqualityComparer : IEqualityComparer<ReferenceWithContext>
    {
        public bool Equals(ReferenceWithContext x, ReferenceWithContext y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(null, x)) return false;
            if (ReferenceEquals(null, y)) return false;

            var xReference = x.GetReference();
            var yReference = y.GetReference();

            return xReference.LinkedElementId == yReference.LinkedElementId
                && xReference.ElementId == yReference.ElementId;
        }

        public int GetHashCode(ReferenceWithContext obj)
        {
            var reference = obj.GetReference();
            unchecked
            {
                return (reference.LinkedElementId.GetHashCode() * 397) ^ reference.ElementId.GetHashCode();
            }
        }
    }
}
