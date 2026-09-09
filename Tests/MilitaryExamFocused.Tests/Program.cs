using System;

internal static class Program
{
    public static void Main()
    {
        MilitaryExamRulesTests.Run();
        ExamCycleRulesTests.Run();
        MilitaryEstablishmentRulesTests.Run();
        MilitaryExamAppointmentRulesTests.Run();
        Console.WriteLine("Military exam focused tests passed.");
    }
}
