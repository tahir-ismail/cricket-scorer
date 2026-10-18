using System.Security.Cryptography;

namespace CricketScorer.Api.Infrastructure;

public static class CodeGenerator
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // Makes a random, hard-to-guess code without look-alike characters (0/O, 1/I).
    public static string New(int length) => RandomNumberGenerator.GetString(Alphabet, length);
}
