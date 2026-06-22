using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace VoiceTranslator.Application.Orchestration;

[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The type intentionally exposes bounded queue semantics.")]
public sealed class BoundedPhraseQueue : IEnumerable<Phrase>
{
    private readonly object syncRoot = new();
    private readonly Queue<Phrase> phrases;
    private readonly int capacity;

    public BoundedPhraseQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
        phrases = new Queue<Phrase>(capacity);
    }

    public void Enqueue(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        lock (syncRoot)
        {
            while (phrases.Count >= capacity)
            {
                phrases.Dequeue();
            }

            phrases.Enqueue(phrase);
        }
    }

    public bool TryDequeue(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out Phrase? phrase)
    {
        lock (syncRoot)
        {
            return phrases.TryDequeue(out phrase);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            phrases.Clear();
        }
    }

    public IEnumerator<Phrase> GetEnumerator()
    {
        lock (syncRoot)
        {
            return phrases.ToArray().AsEnumerable().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
