using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliFx.Infrastructure;

namespace DiscordChatExporter.Commanding;

/// <summary>
/// Implementation of <see cref="IConsole" /> that forwards all output produced by a command to
/// the graphical interface.
/// </summary>
internal sealed class GuiConsole : IConsole, IDisposable
{
    private readonly GuiConsoleStream _outputStream;
    private readonly GuiConsoleStream _errorStream;

    public GuiConsole(GuiRun run)
    {
        Run = run;

        _outputStream = new GuiConsoleStream(run.Append);
        _errorStream = new GuiConsoleStream(run.Append);

        Input = new ConsoleReader(this, Stream.Null);
        Output = new ConsoleWriter(this, _outputStream);
        Error = new ConsoleWriter(this, _errorStream);
    }

    public GuiRun Run { get; }

    public ConsoleReader Input { get; }

    public ConsoleWriter Output { get; }

    public ConsoleWriter Error { get; }

    // The interface displays both streams in the same log, so they are always considered
    // redirected, which also disables terminal-specific behaviors
    public bool IsInputRedirected => true;

    public bool IsOutputRedirected => true;

    public bool IsErrorRedirected => true;

    public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

    public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

    public int WindowWidth { get; set; } = 100;

    public int WindowHeight { get; set; } = 40;

    public int CursorLeft { get; set; }

    public int CursorTop { get; set; }

    public ConsoleKeyInfo ReadKey(bool intercept = false) =>
        throw new NotSupportedException(
            "The command attempted to read interactive input, which is not supported in the graphical interface."
        );

    public void ResetColor()
    {
        ForegroundColor = ConsoleColor.Gray;
        BackgroundColor = ConsoleColor.Black;
    }

    public void Clear() { }

    public CancellationToken RegisterCancellationHandler() => Run.Cancellation.Token;

    public void Dispose()
    {
        Output.Flush();
        Error.Flush();
    }

    /// <summary>
    /// Stream that receives the raw output of a command, decodes it, and forwards it
    /// to the associated run.
    /// </summary>
    private sealed class GuiConsoleStream(Action<string> write) : Stream
    {
        private const string EscapeCharacter = "\u001b";

        private readonly object _lock = new();
        private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length <= 0)
                return;

            lock (_lock)
            {
                var charCount = _decoder.GetCharCount(buffer, flush: false);
                if (charCount <= 0)
                    return;

                var chars = new char[charCount];
                var writtenCharCount = _decoder.GetChars(buffer, chars, flush: false);

                write(Sanitize(new string(chars, 0, writtenCharCount)));
            }
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Removes ANSI escape sequences and normalizes line endings, so that the output can be
        /// displayed verbatim in the browser.
        /// </summary>
        private static string Sanitize(string text)
        {
            var buffer = new StringBuilder(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                // Strip ANSI escape sequences (they are meaningless outside of a terminal)
                if (c == EscapeCharacter[0])
                {
                    i++;

                    if (i < text.Length && text[i] == '[')
                    {
                        // CSI sequence: skip until the terminating letter
                        while (i < text.Length && !char.IsLetter(text[i]))
                            i++;
                    }
                    else if (i < text.Length)
                    {
                        // Escape sequences terminated by a byte in the range 0x40 - 0x5F
                        while (i < text.Length && (text[i] < 0x40 || text[i] > 0x5F))
                            i++;
                    }

                    continue;
                }

                // Carriage returns are used for in-place updates, which don't translate to the
                // log, so they are replaced with regular line breaks
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        continue;

                    buffer.Append('\n');
                    continue;
                }

                buffer.Append(c);
            }

            return buffer.ToString();
        }
    }
}
