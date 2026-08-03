namespace AncientWarfare3.core.lineage
{
    public static class VisibleSurnameRenameSqlRules
    {
        public const int MaxDepth = 128;

        public const string DescendantRelationQuery =
            "WITH RECURSIVE parent_edges(PARENT_ID,CHILD_ID) AS (" +
            "SELECT PARENT_ID_1,ID FROM ActorArchive WHERE PARENT_ID_1>=0 " +
            "UNION SELECT PARENT_ID_2,ID FROM ActorArchive " +
            "WHERE PARENT_ID_2>=0 UNION SELECT PARENT_ID,CHILD_ID " +
            "FROM FamilyEdge)," +
            "descendants(ID,SEX,FATHER_ID,depth) AS (" +
            "SELECT root.ID,root.SEX,-1,0 FROM ActorArchive root " +
            "WHERE root.ID=@root UNION " +
            "SELECT child.ID,child.SEX,parent.ID,branch.depth+1 " +
            "FROM descendants branch JOIN ActorArchive parent " +
            "ON parent.ID=branch.ID AND parent.SEX=0 " +
            "JOIN parent_edges edge ON edge.PARENT_ID=parent.ID " +
            "JOIN ActorArchive child ON child.ID=edge.CHILD_ID " +
            "WHERE branch.depth<@maxDepth) " +
            "SELECT ID,SEX,FATHER_ID FROM descendants " +
            "GROUP BY ID ORDER BY MIN(depth),ID LIMIT @limit";
    }
}
