using System.Collections.Generic;
using System.IO;

namespace AnimeStudio
{
    internal static class ContainerFileStreams
    {
        internal static List<StreamFile> Create(
            SharedBackingStore backingStore,
            IReadOnlyList<BundleFile.Node> directory)
        {
            backingStore.Seal();
            var files = new List<StreamFile>(directory.Count);
            try
            {
                foreach (var node in directory)
                {
                    files.Add(new StreamFile
                    {
                        path = node.path,
                        fileName = Path.GetFileName(node.path),
                        stream = backingStore.CreateSlice(node.offset, node.size)
                    });
                }

                return files;
            }
            catch
            {
                foreach (var file in files)
                {
                    file.stream?.Dispose();
                }

                throw;
            }
        }
    }
}
