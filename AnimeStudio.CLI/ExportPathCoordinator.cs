using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AnimeStudio.CLI
{
    internal sealed class ExportPathCoordinator
    {
        private readonly object sync = new();
        private readonly Dictionary<string, Reservation> reservations =
            new(GetPathComparer());
        private int nextOrdinal;

        internal ExportReservationScope CreateScope(
            int ordinal,
            bool holdOrderUntilDispose)
        {
            return new ExportReservationScope(
                this,
                ordinal,
                holdOrderUntilDispose);
        }

        internal bool TryReserveFile(
            ExportReservationScope scope,
            string directory,
            string fileName,
            string extension,
            bool allowDuplicates,
            out string fullPath)
        {
            lock (sync)
            {
                WaitForOrder(scope);
                for (var suffix = -1; ; suffix++)
                {
                    var candidateName = suffix < 0
                        ? $"{fileName}{extension}"
                        : $"{fileName} ({suffix}){extension}";
                    var candidate = Path.Combine(directory, candidateName);
                    if (TryReserve(scope, candidate, isDirectory: false))
                    {
                        ReleaseOrderAfterInitialReservation(scope);
                        fullPath = candidate;
                        break;
                    }

                    if (!allowDuplicates)
                    {
                        ReleaseOrderAfterInitialReservation(scope);
                        fullPath = candidate;
                        return false;
                    }
                }
            }

            Directory.CreateDirectory(directory);
            return true;
        }

        internal bool TryReserveDirectory(
            ExportReservationScope scope,
            string directory,
            string fileName,
            bool allowDuplicates,
            out string fullPath)
        {
            lock (sync)
            {
                WaitForOrder(scope);
                for (var suffix = -1; ; suffix++)
                {
                    var candidateName = suffix < 0
                        ? fileName
                        : $"{fileName} ({suffix})";
                    var candidate = Path.Combine(directory, candidateName);
                    if (TryReserve(scope, candidate, isDirectory: true))
                    {
                        ReleaseOrderAfterInitialReservation(scope);
                        fullPath = candidate;
                        return true;
                    }

                    if (!allowDuplicates)
                    {
                        ReleaseOrderAfterInitialReservation(scope);
                        fullPath = candidate;
                        return false;
                    }
                }
            }
        }

        internal void Complete(ExportReservationScope scope)
        {
            lock (sync)
            {
                foreach (var path in scope.ReservedPaths)
                {
                    if (!reservations.TryGetValue(path, out var reservation)
                        || !ReferenceEquals(reservation.Owner, scope))
                    {
                        continue;
                    }

                    reservations.Remove(path);
                }

                if (!scope.OrderReleased)
                {
                    WaitForOrder(scope);
                    AdvanceOrder(scope);
                }

                Monitor.PulseAll(sync);
            }
        }

        private bool TryReserve(
            ExportReservationScope scope,
            string path,
            bool isDirectory)
        {
            while (true)
            {
                var exists = isDirectory
                    ? Directory.Exists(path)
                    : File.Exists(path);
                if (exists)
                {
                    return false;
                }

                if (!reservations.TryGetValue(path, out var existing))
                {
                    reservations.Add(
                        path,
                        new Reservation(scope));
                    scope.ReservedPaths.Add(path);
                    return true;
                }

                if (ReferenceEquals(existing.Owner, scope))
                {
                    return false;
                }

                Monitor.Wait(sync);
            }
        }

        private void WaitForOrder(ExportReservationScope scope)
        {
            if (scope.OrderReleased)
            {
                return;
            }

            while (scope.Ordinal != nextOrdinal)
            {
                Monitor.Wait(sync);
            }
        }

        private void ReleaseOrderAfterInitialReservation(
            ExportReservationScope scope)
        {
            if (!scope.HoldOrderUntilDispose && !scope.OrderReleased)
            {
                AdvanceOrder(scope);
            }
        }

        private void AdvanceOrder(ExportReservationScope scope)
        {
            scope.OrderReleased = true;
            nextOrdinal = checked(nextOrdinal + 1);
            Monitor.PulseAll(sync);
        }

        private static StringComparer GetPathComparer()
        {
            return OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        private sealed class Reservation
        {
            internal Reservation(ExportReservationScope owner)
            {
                Owner = owner;
            }

            internal ExportReservationScope Owner { get; }
        }
    }

    internal sealed class ExportReservationScope : IDisposable
    {
        private readonly ExportPathCoordinator coordinator;
        private bool disposed;

        internal ExportReservationScope(
            ExportPathCoordinator coordinator,
            int ordinal,
            bool holdOrderUntilDispose)
        {
            this.coordinator = coordinator;
            Ordinal = ordinal;
            HoldOrderUntilDispose = holdOrderUntilDispose;
        }

        internal int Ordinal { get; }

        internal bool HoldOrderUntilDispose { get; }

        internal bool OrderReleased { get; set; }

        internal List<string> ReservedPaths { get; } = [];

        internal bool TryReserveFile(
            string directory,
            string fileName,
            string extension,
            bool allowDuplicates,
            out string fullPath)
        {
            return coordinator.TryReserveFile(
                this,
                directory,
                fileName,
                extension,
                allowDuplicates,
                out fullPath);
        }

        internal bool TryReserveDirectory(
            string directory,
            string fileName,
            bool allowDuplicates,
            out string fullPath)
        {
            return coordinator.TryReserveDirectory(
                this,
                directory,
                fileName,
                allowDuplicates,
                out fullPath);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            coordinator.Complete(this);
        }
    }
}
