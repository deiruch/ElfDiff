using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using System;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    private static string NormalizeSymbolName(string _s)
    {
        var lastDot = _s.LastIndexOf('.');
        if (lastDot == -1)
            return _s;

        var afterDot = _s[(lastDot + 1)..];
        if (int.TryParse(afterDot, out var i) && i.ToString() == afterDot)
            return $"{_s[..lastDot]}.######";

        return _s;
    }

    enum TypeSortOrder
    {
        BySizeDifference,
        BySize,
        ByName
    }

    private static void ShowUsage()
    {
        System.CommandLine.DragonFruit.CommandLine.InvokeMethod(new[] { "-?" }, System.CommandLine.DragonFruit.EntryPointDiscoverer.FindStaticEntryMethod(Assembly.GetExecutingAssembly()));
    }

    /// <summary>
    /// Compares sizes of two or more ELF binaries, down to individual functions and variables
    /// </summary>
    /// <param name="args">List of at least two .elf binaries</param>
    /// <param name="show_all_sections">Only .text, .data and .bss are shown by default.</param>
    /// <param name="typeSortOrder">Controls the ordering of types in each section</param>
    static void Main(string[] args, bool show_all_sections = false, TypeSortOrder typeSortOrder = TypeSortOrder.BySizeDifference)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return;
        }

        var elfFiles = args.Select(f => ELFReader.Load(f)).ToArray();

        var sections = new[] { ".text", ".data", ".bss" };
        if (show_all_sections)
            sections = elfFiles.SelectMany(ef => ef.Sections.Select(s => s.Name))
                .Distinct()
                .ToArray();
        foreach (var section in sections)
        {
            var t = new Log.Table(new[] { section }.Concat(args).ToArray(), new[] { 40 }.Concat(args.Select(f => f.Length)).ToArray());

            var types = elfFiles.Select(f => f.Sections
                .OfType<SymbolTable<uint>>()
                .SelectMany(st => st.Entries)
                .Where(se => se.PointedSection?.Name == section)
                .ToLookup(se => NormalizeSymbolName(se.Name), se => se))
                .ToArray();

            var typeNames = types.SelectMany(ft => ft).Select(e => e.Key).Distinct();
            switch (typeSortOrder)
            {
                case TypeSortOrder.ByName:
                    typeNames = typeNames.OrderBy(tn => tn);
                    break;
                case TypeSortOrder.BySize:
                    var ordered = typeNames.OrderBy(tn => 0);
                    for (var i = 0; i < types.Length; i++)
                    {
                        var thisI = i;
                        ordered = ordered.ThenByDescending(tn => types[thisI].Contains(tn) ? types[thisI][tn].First().Size : 0);
                    }

                    typeNames = ordered.ThenBy(tn => tn);
                    break;
                case TypeSortOrder.BySizeDifference:
                    typeNames = typeNames.OrderBy(tn =>
                            {
                                var sizeDiff = types[0].Contains(tn) ? (long)types[0][tn].First().Size : 0;
                                for (var i = 1; i < types.Length; i++)
                                    if(types[i].Contains(tn))
                                        sizeDiff -= types[i][tn].First().Size;

                                return sizeDiff;
                            })
                        .ThenBy(tn => tn);
                    break;
            }

            foreach (var typeName in typeNames)
            {
                var symbolSizes = types.Select(ft => (ft.Contains(typeName) ? ft[typeName].First() : null)?.Size).ToArray();
                if (symbolSizes.Distinct().Count() > 1)
                {
                    t.WriteLine(new object?[] { typeName }
                        .Concat(symbolSizes.Cast<object>())
                        .ToArray());
                }
            }


            var sectionSizes = elfFiles.Select(ef => ef.TryGetSection(section, out var s) ? (Section<uint>)s : null)
                .Select(fs => fs?.Size)
                .ToArray();
            if (!show_all_sections || sectionSizes.Distinct().Count() > 1)
                t.WriteBoldLine(new object?[] { "Total" }
                    .Concat(sectionSizes.Cast<object>())
                    .ToArray());

            Console.WriteLine();
        }




        //    foreach (var type in allTypes)
        //    {
        //        var sizes = fileSections
        //                .Select(fs => (fs is SymbolTable<uint> st ? st.Entries : null)
        //                    ?.FirstOrDefault(e => e.Name == type.Name
        //                        && e.Binding == type.Binding
        //                        && e.Type == type.Type
        //                        && e.Visibility == type.Visibility)
        //                    ?.Size);

        //        if (sizes.Distinct().Count() > 1)
        //            t.WriteLine(new object?[] { section, type.Name }.Concat(sizes.Cast<object>()).ToArray());
        //    }


        foreach (var ef in elfFiles)
            ef.Dispose();
    }
}