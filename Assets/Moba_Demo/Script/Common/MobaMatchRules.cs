namespace MobaTypes
{
    // MatchService 풀 크기 / UI "n/m" 표시 / Practice=1
    public static class MobaMatchRules
    {
        public static int GetRequiredPlayerCount(GameType type)
        {
            return type switch
            {
                GameType.Practice => 1,
                GameType.OneVsOne => 2,
                GameType.FiveVsFive => 10,
                GameType.FreeForAll => 8,
                _ => -1
            };
        }
    }
}
