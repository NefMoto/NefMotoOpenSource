/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

Contact by Email: nyet@nyet.org
*/

using System.Reflection;
using System.Text.RegularExpressions;
using Communication;
using Shared;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    /// <summary>
    /// A modal prompt on the K-line thread stops tester present. KWP code must
    /// post with BeginDisplayUserPrompt. A UI completion must use BeginInvoke,
    /// including when the dialog is inside a method the completion calls.
    /// DisplayUserPrompt's own Dispatcher.Invoke is the marshal onto the UI thread.
    /// A RequestUpload or RequestDownload with no reply must finish the action,
    /// and completing the operation must abort an action that is still in flight.
    /// </summary>
    public sealed class BlockingUserPromptTests
    {
        [Fact]
        public void Kwp_code_does_not_wait_on_DisplayUserPrompt()
        {
            var hits = new List<string>();
            foreach (string file in SourceFiles(Path.Combine(RepoRoot(), "Communication")))
            {
                string text = File.ReadAllText(file);
                int search = 0;
                while ((search = text.IndexOf(".DisplayUserPrompt(", search, StringComparison.Ordinal)) >= 0)
                {
                    hits.Add(Rel(file) + ":" + LineNumber(text, search));
                    search++;
                }
            }

            Assert.True(hits.Count == 0, "Blocking DisplayUserPrompt:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
        }

        [Fact]
        public void Modal_prompt_is_not_inside_Dispatcher_Invoke()
        {
            var hits = new List<string>();
            foreach (string project in new[] { "Communication", "ECUFlasher" })
            {
                foreach (string file in SourceFiles(Path.Combine(RepoRoot(), project)))
                {
                    hits.AddRange(BlockingInvokes(File.ReadAllText(file), Rel(file)));
                }
            }

            Assert.True(hits.Count == 0, "Prompt inside Dispatcher.Invoke:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
        }

        [Fact]
        public void Scanner_flags_a_prompt_inside_Invoke_and_allows_BeginInvoke()
        {
            const string blocking = "void Done() { Dispatcher.Invoke((Action)(() => { DisplayUserPrompt(\"title\", \"message\", UserPromptType.OK); })); }";
            const string posted = "void Done() { Dispatcher.BeginInvoke((Action)(() => { DisplayUserPrompt(\"title\", \"message\", UserPromptType.OK); })); }";
            const string helper = "void Done() { Dispatcher.Invoke((Action)(() => { ShowFinished(); })); } void ShowFinished() { DisplayUserPrompt(\"title\", \"message\", UserPromptType.OK); }";

            Assert.NotEmpty(BlockingInvokes(blocking, "sample.cs"));
            Assert.Empty(BlockingInvokes(posted, "sample.cs"));
            Assert.NotEmpty(BlockingInvokes(helper, "sample.cs"));
        }

        [Fact]
        public void OperationCompleted_aborts_the_in_flight_action()
        {
            var iface = ConnectedInterface(new List<string>());
            var action = new HangingAction(iface);
            var operation = new SingleActionOperation(iface, action);

            operation.Start();
            Assert.True(operation.IsRunning);
            Assert.False(action.IsComplete);
            Assert.Equal(1, operation.NextActionCalls);

            operation.Abort();

            Assert.False(operation.IsRunning);
            Assert.True(action.IsComplete);
            Assert.Equal(1, operation.NextActionCalls);

            Deliver(iface, new KWP2000Message(
                KWP2000AddressMode.Physical,
                0x10,
                0xF1,
                (byte)KWP2000ServiceID.TesterPresent,
                new byte[] { 0x01 }));
            Assert.Equal(0, action.MessagesSeen);
        }

        [Fact]
        public void RequestUpload_no_reply_completes_the_action()
        {
            AssertNoReplyCompletes(iface => new RequestUploadFromECUAction(
                iface,
                0x800000,
                64,
                TransferDataAction.CompressionType.Uncompressed,
                TransferDataAction.EncryptionType.Unencrypted));
        }

        [Fact]
        public void RequestDownload_no_reply_completes_the_action()
        {
            AssertNoReplyCompletes(iface => new RequestDownloadToECUAction(
                iface,
                0x800000,
                64,
                TransferDataAction.CompressionType.Uncompressed,
                TransferDataAction.EncryptionType.Unencrypted));
        }

        [Fact]
        public void Layout_validation_probe_no_reply_completes_the_action()
        {
            AssertNoReplyCompletes(iface => new ValidateStartAndEndAddressesWithRequestUploadDownloadAction(
                iface,
                0x800000,
                0x900000));
        }

        [Fact]
        public void RequestUpload_with_a_reply_stays_running()
        {
            var log = new List<string>();
            var iface = ConnectedInterface(log);
            var action = new RequestUploadFromECUAction(
                iface,
                0x800000,
                64,
                TransferDataAction.CompressionType.Uncompressed,
                TransferDataAction.EncryptionType.Unencrypted);

            Finish(iface, action, receivedAnyReplies: true);

            Assert.False(action.IsComplete);
            Assert.DoesNotContain(log, line => line.Contains("Did not receive any replies to message"));
        }

        private static IEnumerable<string> BlockingInvokes(string text, string relativePath)
        {
            var promptMethods = PromptMethodNames(text);
            int search = 0;
            while ((search = text.IndexOf("Dispatcher.Invoke", search, StringComparison.Ordinal)) >= 0)
            {
                int open = text.IndexOf('(', search);
                if (open < 0)
                {
                    yield break;
                }

                int close = MatchCloser(text, open, '(', ')');
                if (InsideMethod(text, search, "DisplayUserPrompt"))
                {
                    search = close + 1;
                    continue;
                }

                string args = text.Substring(open, close - open + 1);
                if (args.Contains("DisplayUserPrompt(", StringComparison.Ordinal)
                    || promptMethods.Any(name => args.Contains(name + "(", StringComparison.Ordinal)))
                {
                    yield return relativePath + ":" + LineNumber(text, search);
                }

                search = close + 1;
            }
        }

        private static List<string> PromptMethodNames(string text)
        {
            var names = new List<string>();
            foreach (Match method in MethodPattern.Matches(text))
            {
                int paren = text.IndexOf('(', method.Index);
                int parenClose = MatchCloser(text, paren, '(', ')');
                int brace = text.IndexOf('{', parenClose);
                if ((brace < 0) || (brace - parenClose > 400))
                {
                    continue;
                }

                int braceClose = MatchCloser(text, brace, '{', '}');
                string body = text.Substring(brace, braceClose - brace + 1);
                string name = method.Groups[1].Value;
                if ((name != "DisplayUserPrompt") && body.Contains("DisplayUserPrompt(", StringComparison.Ordinal))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static bool InsideMethod(string text, int index, string methodName)
        {
            foreach (Match method in MethodPattern.Matches(text))
            {
                if (method.Groups[1].Value != methodName)
                {
                    continue;
                }

                int paren = text.IndexOf('(', method.Index);
                int parenClose = MatchCloser(text, paren, '(', ')');
                int brace = text.IndexOf('{', parenClose);
                if ((brace < 0) || (index < brace))
                {
                    continue;
                }

                int braceClose = MatchCloser(text, brace, '{', '}');
                if (index <= braceClose)
                {
                    return true;
                }
            }

            return false;
        }

        private static int MatchCloser(string text, int openIndex, char open, char close)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipString(text, i);
                    continue;
                }

                if ((c == '/') && (i + 1 < text.Length) && (text[i + 1] == '/'))
                {
                    int newline = text.IndexOf('\n', i);
                    i = newline < 0 ? text.Length - 1 : newline;
                    continue;
                }

                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return text.Length - 1;
        }

        private static int SkipString(string text, int quoteIndex)
        {
            char quote = text[quoteIndex];
            for (int i = quoteIndex + 1; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (text[i] == quote)
                {
                    return i;
                }
            }

            return text.Length - 1;
        }

        private static IEnumerable<string> SourceFiles(string directory)
        {
            return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => path.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Communication", "KWP2000Operations.cs")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not find the repository root from " + AppContext.BaseDirectory);
        }

        private static string Rel(string path)
        {
            return Path.GetRelativePath(RepoRoot(), path);
        }

        private static int LineNumber(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static readonly Regex MethodPattern = new Regex(@"\b(?:void|bool|int|string|UserPromptResult)\s+(\w+)\s*\(", RegexOptions.Compiled);

        private static void AssertNoReplyCompletes(Func<QueueKwp, KWP2000Action> create)
        {
            var log = new List<string>();
            var iface = ConnectedInterface(log);
            var action = create(iface);

            Finish(iface, action, receivedAnyReplies: false);

            Assert.True(action.IsComplete);
            Assert.False(action.CompletedSuccessfully);
            Assert.Contains(log, line => line.Contains("Did not receive any replies to message"));
        }

        private static void Finish(QueueKwp iface, KWP2000Action action, bool receivedAnyReplies)
        {
            Assert.True(action.Start());

            KWP2000Message message = iface.OnlyPending();
            MulticastDelegate? finished = message.GetResponsesFinishedEvent();
            Assert.NotNull(finished);

            finished.DynamicInvoke(iface, message, true, receivedAnyReplies, true, (uint)0);
        }

        private static QueueKwp ConnectedInterface(List<string> log)
        {
            var iface = new QueueKwp(log);
            iface.ConnectionStatus = CommunicationInterface.ConnectionStatusType.Connected;
            return iface;
        }

        private static void Deliver(KWP2000Interface iface, KWP2000Message message)
        {
            FieldInfo? field = typeof(KWP2000Interface).GetField(
                "ReceivedMessageEvent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            var handlers = (Delegate?)field.GetValue(iface);
            handlers?.DynamicInvoke(iface, message);
        }

        private sealed class QueueKwp : KWP2000Interface
        {
            public QueueKwp(List<string> log)
                : base((message, type) => log.Add(message))
            {
            }

            public KWP2000Message OnlyPending()
            {
                return Assert.Single(mMessagesPendingSend);
            }
        }

        private sealed class HangingAction : KWP2000Action
        {
            public HangingAction(KWP2000Interface commInterface)
                : base(commInterface)
            {
            }

            public int MessagesSeen { get; private set; }

            protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
            {
                MessagesSeen++;
                return true;
            }
        }

        private sealed class SingleActionOperation : CommunicationOperation
        {
            private readonly CommunicationAction _action;

            public SingleActionOperation(CommunicationInterface commInterface, CommunicationAction action)
                : base(commInterface)
            {
                _action = action;
            }

            public int NextActionCalls { get; private set; }

            protected override CommunicationAction NextAction()
            {
                NextActionCalls++;
                if (NextActionCalls == 1)
                {
                    return _action;
                }

                return new HangingAction((KWP2000Interface)CommInterface);
            }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
