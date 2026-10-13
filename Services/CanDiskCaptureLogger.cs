using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace J2534Diag
{
    /// <summary>
    /// Disk-writer accounting: these counters describe application-level
    /// persistence only, not completeness of the vehicle's CAN traffic.
    /// </summary>
    public sealed class CanDiskLogStatus
    {
        public long Accepted { get; internal set; }
        public long Written { get; internal set; }
        public long QueueFullDrops { get; internal set; }
        public long RejectedAfterFailure { get; internal set; }
        public long AcceptedNotWritten { get; internal set; }
        public string WriterError { get; internal set; }
        public bool Closed { get; internal set; }

        public bool ApplicationWriteComplete =>
            Closed && WriterError == null && QueueFullDrops == 0 &&
            RejectedAfterFailure == 0 && AcceptedNotWritten == 0;
    }

    /// <summary>
    /// One writer thread, bounded in-memory queue, explicit accounting.
    /// Enqueue never blocks the J2534 receive thread on filesystem I/O.
    /// This is NOT a guarantee of hardware-level lossless CAN recording.
    /// </summary>
    public sealed class CanDiskCaptureLogger
    {
        private readonly BlockingCollection<CanCaptureRecord> _queue;
        private readonly TextWriter _writer;
        private readonly bool _ownsWriter;
        private readonly Thread _writerThread;
        private readonly object _completionLock = new object();
        private bool _completionRequested;
        private long _accepted;
        private long _written;
        private long _queueFullDrops;
        private long _rejectedAfterFailure;
        private Exception _writerError;
        private bool _closed;

        public string FilePath { get; }

        public CanDiskCaptureLogger(string filePath, int queueCapacity = 4096)
            : this(
                new StreamWriter(
                    new FileStream(filePath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.Read, 64 * 1024),
                    new UTF8Encoding(false), 64 * 1024),
                queueCapacity, true, Path.GetFullPath(filePath))
        {
        }

        // Injectable writer for deterministic offline tests only.
        public CanDiskCaptureLogger(TextWriter writer, int queueCapacity, bool ownsWriter = false)
            : this(writer, queueCapacity, ownsWriter, null)
        {
        }

        private CanDiskCaptureLogger(TextWriter writer, int queueCapacity, bool ownsWriter, string filePath)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            if (queueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            _ownsWriter = ownsWriter;
            FilePath = filePath;
            _queue = new BlockingCollection<CanCaptureRecord>(
                new ConcurrentQueue<CanCaptureRecord>(), queueCapacity);
            _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "CAN CSV disk writer" };
            _writerThread.Start();
        }

        public bool TryEnqueue(CanCaptureRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            lock (_completionLock)
            {
                if (_completionRequested || Volatile.Read(ref _writerError) != null)
                {
                    Interlocked.Increment(ref _rejectedAfterFailure);
                    return false;
                }

                if (!_queue.TryAdd(record))
                {
                    Interlocked.Increment(ref _queueFullDrops);
                    return false;
                }

                Interlocked.Increment(ref _accepted);
                return true;
            }
        }

        private void WriteLoop()
        {
            try
            {
                CanCaptureCsv.WriteHeader(_writer);
                foreach (var record in _queue.GetConsumingEnumerable())
                {
                    CanCaptureCsv.WriteRecord(_writer, record);
                    long count = Interlocked.Increment(ref _written);
                    if (count % 128 == 0) _writer.Flush();
                }
                _writer.Flush();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _writerError, ex);
            }
            finally
            {
                if (_ownsWriter)
                {
                    try { _writer.Dispose(); }
                    catch (Exception ex)
                    {
                        if (Volatile.Read(ref _writerError) == null)
                            Volatile.Write(ref _writerError, ex);
                    }
                }
                _closed = true;
            }
        }

        /// <summary>
        /// Finish only after the receive thread has stopped.
        /// Returns false on timeout; caller MUST retain this logger and cannot
        /// claim a flushed/closed capture until it completes.
        /// </summary>
        public bool CompleteAndWait(int timeoutMilliseconds = 10000)
        {
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            lock (_completionLock)
            {
                if (!_completionRequested)
                {
                    _completionRequested = true;
                    _queue.CompleteAdding();
                }
            }
            return _writerThread.Join(timeoutMilliseconds);
        }

        public CanDiskLogStatus GetStatus()
        {
            long accepted = Interlocked.Read(ref _accepted);
            long written = Interlocked.Read(ref _written);
            return new CanDiskLogStatus
            {
                Accepted = accepted,
                Written = written,
                QueueFullDrops = Interlocked.Read(ref _queueFullDrops),
                RejectedAfterFailure = Interlocked.Read(ref _rejectedAfterFailure),
                AcceptedNotWritten = Math.Max(0L, accepted - written),
                WriterError = Volatile.Read(ref _writerError)?.Message,
                Closed = _closed && !_writerThread.IsAlive
            };
        }
    }
}
