using System.Data.SQLite;
using AncientWarfare3.api.history;
using AncientWarfare3.core.db;
using AncientWarfare3.core.court;
using AncientWarfare3.core.historyapi;

internal static class Program {
    private static int Main() {
        var cases = new (string Name, Action Run)[] {
            ("world change during callback invalidates old batch", WorldChangeDuringCallback),
            ("reentrant drain preserves event ordering", ReentrantDrain),
            ("committed biography publishes actor identity", CommittedBiographyIdentity),
            ("global history continues past 512 rows", GlobalPagination),
            ("filter before page limit", FilterBeforeLimit),
            ("career actor and office cursors", CareerPagination),
            ("diplomacy response cursors", DiplomacyPagination),
            ("same-time multi-source cursor ordering", MixedCursorOrdering),
            ("scoped biography and chronicle cursors", ScopedHistoryPagination),
            ("official career end events use their own time", CareerEndPagination),
            ("invalid times and cursors", NullTimeAndInvalidCursor),
            ("publisher clear resets identities and old batch", PublisherWorldChange),
            ("actor query excludes unrelated diplomacy", ActorScope),
            ("career scope keeps legacy city fallback", CareerScope),
            ("diplomacy lifecycle fields remain stable", DiplomacyMappings)
        };
        int failed = 0;
        foreach (var test in cases) try { test.Run(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        Console.WriteLine($"History API runtime: {cases.Length - failed}/{cases.Length} passed.");
        return failed == 0 ? 0 : 1;
    }
    private static AW3HistoryEvent Event(long id) => new(id,"biography","PersonBiography",id,"born",id,1,"",7,-1,9,9,"","","","");
    private static void WorldChangeDuringCallback() {
        var registry = new AW3HistorySubscriptionRegistry(); int oldCalls=0, newCalls=0;
        registry.Subscribe(null, _ => { registry.Clear(); registry.Subscribe(null, _ => newCalls++); registry.PublishCommitted(Event(2)); });
        registry.Subscribe(null, _ => oldCalls++);
        registry.PublishCommitted(Event(1)); registry.Drain();
        Require(oldCalls==0, "world reset must invalidate subscribers already captured by Drain");
        Require(newCalls==0 && registry.PendingCount==1,"old drain must leave new-world notifications queued");
        registry.Drain(); Require(newCalls==1,"new world event is delivered on its own drain");
    }
    private static void ReentrantDrain() {
        var registry = new AW3HistorySubscriptionRegistry(); var ids=new List<long>();
        registry.Subscribe(null, _ => registry.Drain()); registry.Subscribe(null,e=>ids.Add(e.RecordId));
        registry.PublishCommitted(Event(1)); registry.PublishCommitted(Event(2)); registry.Drain();
        Require(ids.SequenceEqual(new long[]{1,2}),"subscriber-triggered drain must not invert committed event order");
    }
    private static void CommittedBiographyIdentity() {
        WithDb(db=>{
            Exec(db,"INSERT INTO PersonBiography(EVENT_ID,ACTOR_ID,WORLD_TIME,EVENT_TYPE,CONTEXT_KINGDOM_ID) VALUES(1,7,1,'born',9)");
            AW3HistoryEventPublisher.Clear(); int calls=0;
            using var handle=AW3HistoryApi.Subscribe(AW3HistorySubscription.Create(actorId:7),e=>{Require(e.SubjectId==7,"actor identity");calls++;});
            AW3HistoryEventPublisher.PublishHistoryRow("PersonBiography",1); AW3HistoryEventPublisher.Drain();
            Require(calls==1,"canonical history publication must reach actor subscriptions");
            Exec(db,"INSERT INTO KingdomHistory(EVENT_ID,KINGDOM_ID,WORLD_TIME,EVENT_TYPE,PROJECTION_KEY) VALUES(1,9,2,'founded','kingdom-key'); INSERT INTO CityHistory(EVENT_ID,CITY_ID,WORLD_TIME,EVENT_TYPE,CONTEXT_KINGDOM_ID,PROJECTION_KEY) VALUES(1,3,3,'founded',9,'city-key')");
            foreach (string table in new[]{"PersonBiography","KingdomHistory","CityHistory"}) {
                var published=AW3HistoryReadService.ReadCommittedRow(table,1);
                var queried=AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create()).Items.Single(e=>e.Source==table);
                Require(published.SubjectId==queried.SubjectId && published.KingdomId==queried.KingdomId && published.ProjectionKey==queried.ProjectionKey && published.ProjectionKeyText==queried.ProjectionKeyText,
                    "published history identity and projection must match queried DTO for " + table);
            }
        });
    }
    private static void GlobalPagination() {
        WithDb(db=>{
            Exec(db,"WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<600) INSERT INTO PersonBiography(EVENT_ID,ACTOR_ID,WORLD_TIME,EVENT_TYPE) SELECT i,7,i,'born' FROM seq");
            var ids=ReadAll(cursor=>AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create(domain:"biography",limit:128,cursor:cursor)),e=>e.RecordId);
            Require(ids.SequenceEqual(Enumerable.Range(1,600).Select(x=>(long)x)),"all 600 events must be reachable once in chronological order");
        });
    }
    private static void FilterBeforeLimit() {
        WithDb(db=>{
            Exec(db,"WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<600) INSERT INTO KingdomHistory(EVENT_ID,KINGDOM_ID,WORLD_TIME,EVENT_TYPE) SELECT i,9,i,CASE WHEN i>550 THEN 'ruler' ELSE 'founded' END FROM seq");
            var page=AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create(domain:"chronicle",eventType:"ruler",worldTimeFrom:550,limit:10));
            Require(page.Items.Count==10 && page.Items[0].RecordId==551 && page.HasMore,"SQL must filter late matching events before taking page lookahead");
        });
    }
    private static void CareerPagination() {
        WithDb(db=>{
            Exec(db,"WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<600) INSERT INTO CourtOfficer(OFFICER_ID,KINGDOM_ID,ACTOR_ID,CITY_ID,COUNTY_ID,LAYER,OFFICE_ID,APPOINTED_YEAR,APPOINTED_TIME,ENDED_TIME) SELECT i,9,7,3,4,'central','minister',i,i,-1 FROM seq");
            foreach(bool office in new[]{false,true}) {
                var ids=ReadAll(cursor=>office ? AW3OfficialCareerApi.GetOfficeHistory(9,"central","minister",AW3HistoryQuery.Create(limit:128,cursor:cursor)) : AW3OfficialCareerApi.GetHistory(7,AW3HistoryQuery.Create(limit:128,cursor:cursor)),e=>e.OfficerId);
                Require(ids.SequenceEqual(Enumerable.Range(1,600).Select(x=>(long)x)),"actor/office career must not truncate to latest query.Limit rows");
            }
        });
    }
    private static void DiplomacyPagination() {
        WithDb(db=>{
            Exec(db,"WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<600) INSERT INTO DiplomacyProposal(PROPOSAL_ID,REQUESTER_KINGDOM_ID,RESPONDER_KINGDOM_ID,CREATED_TIME,RESPONSE_TIME,PROPOSAL_TYPE,STATUS) SELECT i,9,10,i,1000+i,'alliance','accepted' FROM seq");
            var ids=ReadAll(cursor=>AW3DiplomacyHistoryApi.GetEventsBetween(9,10,AW3HistoryQuery.Create(eventType:"proposal_response",limit:128,cursor:cursor)),e=>e.RecordId);
            Require(ids.SequenceEqual(Enumerable.Range(1,600).Select(x=>(long)x)),"response pagination must use response chronology beyond first 512 proposals");
        });
    }
    private static void MixedCursorOrdering() {
        WithDb(db=>{
            Exec(db,"INSERT INTO PersonBiography(EVENT_ID,ACTOR_ID,WORLD_TIME,EVENT_TYPE) VALUES(1,7,5,'born'),(2,7,5,'born'); INSERT INTO CityHistory(EVENT_ID,CITY_ID,WORLD_TIME,EVENT_TYPE) VALUES(1,3,5,'founded'); INSERT INTO KingdomHistory(EVENT_ID,KINGDOM_ID,WORLD_TIME,EVENT_TYPE) VALUES(1,9,5,'founded')");
            var ids=ReadAll(cursor=>AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create(limit:1,cursor:cursor)),e=>e.Source+":"+e.RecordId);
            Require(ids.SequenceEqual(new[]{"PersonBiography:1","PersonBiography:2","CityHistory:1","KingdomHistory:1"}),"equal-time cursor must account for domain/source before record ID");
        });
    }
    private static void ScopedHistoryPagination() {
        WithDb(db=>{
            Exec(db,"WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<600) INSERT INTO PersonBiography(EVENT_ID,ACTOR_ID,WORLD_TIME,EVENT_TYPE) SELECT i,7,i,'born' FROM seq; INSERT INTO CityHistory(EVENT_ID,CITY_ID,WORLD_TIME,EVENT_TYPE) SELECT EVENT_ID,3,WORLD_TIME,'founded' FROM PersonBiography; INSERT INTO KingdomHistory(EVENT_ID,KINGDOM_ID,WORLD_TIME,EVENT_TYPE) SELECT EVENT_ID,9,WORLD_TIME,'founded' FROM PersonBiography");
            var expected=Enumerable.Range(1,600).Select(x=>(long)x);
            Require(ReadAll(cursor=>AW3BiographyApi.GetEntries(7,AW3HistoryQuery.Create(limit:512,cursor:cursor)),e=>e.RecordId).SequenceEqual(expected),"biography endpoint must reach second page");
            Require(ReadAll(cursor=>AW3ChronicleApi.GetKingdomEvents(9,AW3HistoryQuery.Create(limit:512,cursor:cursor)),e=>e.RecordId).SequenceEqual(expected),"kingdom endpoint must reach second page");
            Require(ReadAll(cursor=>AW3ChronicleApi.GetCityEvents(3,AW3HistoryQuery.Create(limit:512,cursor:cursor)),e=>e.RecordId).SequenceEqual(expected),"city endpoint must reach second page");
        });
    }
    private static void CareerEndPagination() {
        WithDb(db=>{
            Exec(db,"WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<600) INSERT INTO CourtOfficer(OFFICER_ID,KINGDOM_ID,ACTOR_ID,APPOINTED_TIME,ENDED_TIME) SELECT i,9,7,i,2000-i FROM seq");
            var ids=ReadAll(cursor=>AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create(domain:"official_career",eventType:"office_ended",limit:128,cursor:cursor)),e=>e.RecordId);
            Require(ids.SequenceEqual(Enumerable.Range(1,600).Reverse().Select(x=>(long)x)),"end events must sort on end time independent of appointment order");
        });
    }
    private static void NullTimeAndInvalidCursor() {
        WithDb(db=>{
            Exec(db,"INSERT INTO PersonBiography(EVENT_ID,ACTOR_ID,WORLD_TIME,EVENT_TYPE) VALUES(1,7,NULL,'born'),(2,7,-1,'born'),(3,7,0,'born'),(4,7,1e999,'born'),(5,7,-1e999,'born')");
            var ids=ReadAll(cursor=>AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create(domain:"biography",limit:1,cursor:cursor)),e=>e.RecordId);
            Require(ids.SequenceEqual(new long[]{1,2,4,5,3}),"SQL finite-time normalization must match cursor ordering");
            var page=AW3HistoryApi.ReadEvents(AW3HistoryQuery.Create(cursor:"invalid"));
            Require(page.Items.Count==0 && !page.HasMore,"malformed cursor must not restart from beginning");
        });
    }
    private static void PublisherWorldChange() {
        AW3HistoryEventPublisher.Clear(); int stale=0, current=0;
        using var first=AW3HistoryEventPublisher.Subscribe(null,_=>{
            AW3HistoryEventPublisher.Clear(); AW3HistoryEventPublisher.Subscribe(null,_=>current++);
            AW3HistoryEventPublisher.PublishCommitted(Event(1));
        });
        using var second=AW3HistoryEventPublisher.Subscribe(null,_=>stale++);
        AW3HistoryEventPublisher.PublishCommitted(Event(1)); AW3HistoryEventPublisher.Drain();
        Require(stale==0 && current==0,"publisher world reset stops captured callbacks and leaves new events queued");
        AW3HistoryEventPublisher.Drain();
        Require(current==1,"same event ID can be published after world reset");
        AW3HistoryEventPublisher.PublishCommitted(Event(1)); AW3HistoryEventPublisher.Drain();
        Require(current==1,"duplicate identity remains suppressed within new world");
        AW3HistoryEventPublisher.Clear();
    }
    private static void ActorScope() {
        WithDb(db=>{
            Exec(db,"INSERT INTO PersonBiography(EVENT_ID,ACTOR_ID,WORLD_TIME,EVENT_TYPE) VALUES(1,7,1,'born'); INSERT INTO DiplomacyProposal(PROPOSAL_ID,REQUESTER_KINGDOM_ID,RESPONDER_KINGDOM_ID,CREATED_TIME,RESPONSE_TIME) VALUES(1,9,10,1,-1)");
            var page=AW3HistoryApi.ReadEvents(AW3HistoryQuery.ForActor(7));
            Require(page.Items.Count==1 && page.Items.All(e=>e.SubjectId==7),"actor-filtered history must not include unrelated kingdom diplomacy");
        });
    }
    private static void CareerScope() {
        WithDb(db=>{
            Exec(db,"INSERT INTO CourtOfficer(OFFICER_ID,KINGDOM_ID,ACTOR_ID,CITY_ID,COUNTY_ID,LAYER,OFFICE_ID,APPOINTED_TIME) VALUES(1,9,7,3,4,'local','magistrate',1),(2,9,7,-1,4,'local','magistrate',2),(3,9,7,5,4,'local','magistrate',3),(4,9,7,3,5,'local','magistrate',4),(5,9,7,3,4,'local','general',5)");
            var scope=new OfficialCareerHistoryScope(9,3,"local","magistrate",4);
            var ids=ReadAll(cursor=>AW3HistoryReadService.ReadCareer(null,scope,AW3HistoryQuery.Create(limit:1,cursor:cursor)),e=>e.OfficerId);
            Require(ids.SequenceEqual(new long[]{1,2}),"scope-only query retains old unassigned-city history fallback and county/office restriction");
            var page=AW3OfficialCareerApi.GetOfficeHistory(9,"local","magistrate",AW3HistoryQuery.Create(cityId:3,countyId:4));
            Require(page.Items.Count==1 && page.Items[0].OfficerId==1,"explicit query city filter retains its previous exact-match semantics");
        });
    }
    private static void DiplomacyMappings() {
        WithDb(db=>{
            Exec(db,"CREATE TABLE DiplomacyDialogue(EVENT_ID INTEGER,KINGDOM_A_ID INTEGER,KINGDOM_B_ID INTEGER,EVENT_TYPE TEXT,DETAIL TEXT,EVENT_YEAR INTEGER,EVENT_TIME REAL,YEAR_PREFIX TEXT);"+
                "CREATE TABLE DiplomaticMarriage(MARRIAGE_ID INTEGER,KINGDOM_A_ID INTEGER,KINGDOM_B_ID INTEGER,START_YEAR INTEGER,START_TIME REAL,END_TIME REAL);"+
                "CREATE TABLE DiplomaticOperation(OPERATION_ID INTEGER,SOURCE_KINGDOM_ID INTEGER,TARGET_KINGDOM_ID INTEGER,OPERATION_TYPE TEXT,START_YEAR INTEGER,START_TIME REAL,DUE_TIME REAL,RESULT TEXT,STATUS TEXT);"+
                "CREATE TABLE DiplomaticCoalition(COALITION_ID INTEGER,MEMBER_A_ID INTEGER,MEMBER_B_ID INTEGER,START_YEAR INTEGER,START_TIME REAL,END_TIME REAL,STATUS TEXT);"+
                "CREATE TABLE PeaceSettlement(SETTLEMENT_ID INTEGER,WINNER_KINGDOM_ID INTEGER,LOSER_KINGDOM_ID INTEGER,WORLD_TIME REAL,TERMS_TEXT TEXT);"+
                "INSERT INTO DiplomacyDialogue VALUES(1,9,10,'greeting','detail',5,1,'year5');"+
                "INSERT INTO DiplomacyProposal(PROPOSAL_ID,REQUESTER_KINGDOM_ID,RESPONDER_KINGDOM_ID,PROPOSAL_TYPE,STATUS,CREATED_TIME,RESPONSE_TIME,RESPONSE_REASON) VALUES(1,9,10,'alliance','accepted',1,2,'reason');"+
                "INSERT INTO DiplomaticMarriage VALUES(1,9,10,5,1,2);"+
                "INSERT INTO DiplomaticOperation VALUES(1,9,10,'mission',5,1,2,'success','finished');"+
                "INSERT INTO DiplomaticCoalition VALUES(1,9,10,5,1,2,'ended');"+
                "INSERT INTO PeaceSettlement VALUES(1,9,10,2,'terms')");
            var items=ReadAll(cursor=>AW3DiplomacyHistoryApi.GetEventsBetween(9,10,AW3HistoryQuery.Create(limit:1,cursor:cursor)),e=>e);
            Require(items.Count==10 && items.Select(e=>e.Source).Distinct().Count()==10,"all ten lifecycle projections remain reachable across equal-time sources");
            var response=items.Single(e=>e.Source=="DiplomacyProposalResponse");
            Require(response.FirstKingdomId==10 && response.SecondKingdomId==9 && response.Status=="accepted" && response.Content=="reason","proposal response keeps respondent first and recorded status/detail");
            var dialogue=items.Single(e=>e.Source=="DiplomacyDialogue");
            Require(dialogue.EventType=="dialogue:greeting" && dialogue.YearText=="year5" && dialogue.WorldYear==5 && dialogue.Content=="detail","dialogue preserves prefix/time/type/detail mapping");
            Require(items.Single(e=>e.Source=="DiplomaticOperationResult").Content=="success","operation result content retained");
            Require(items.Single(e=>e.Source=="PeaceSettlement").Content=="terms","settlement terms retained");
        });
    }
    private static List<TResult> ReadAll<T,TResult>(Func<string,AW3HistoryPage<T>> read,Func<T,TResult> select) {
        var result=new List<TResult>(); string cursor="";
        for(int n=0;n<100;n++) { var page=read(cursor); result.AddRange(page.Items.Select(select)); if(!page.HasMore) return result; Require(page.Items.Count>0 && page.NextCursor!=cursor,"cursor must progress"); cursor=page.NextCursor; }
        throw new Exception("pagination did not terminate");
    }
    private static void WithDb(Action<SQLiteConnection> action) {
        string path=Path.Combine(Path.GetTempPath(),"aw3-history-api-"+Guid.NewGuid()+".db");
        try {
            using var db=new SQLiteConnection(new SQLiteConnectionStringBuilder{DataSource=path,Pooling=false}.ConnectionString); db.Open();
            LineageArchiveManager.RuntimeDbPath=path; LineageArchiveManager.RuntimeDatabaseEpoch++; LineageArchiveManager.Instance.OperatingDB=db;
            Exec(db,"CREATE TABLE PersonBiography(EVENT_ID INTEGER PRIMARY KEY,ACTOR_ID INTEGER,WORLD_TIME REAL,YEAR_PREFIX TEXT,EVENT_TYPE TEXT,SUBJECT_NAME TEXT,CONTENT TEXT,CATEGORY TEXT,AGE_AT_EVENT INTEGER,IS_KING_AT_EVENT INTEGER,ROLE_SNAPSHOT TEXT,CONTEXT_KINGDOM_ID INTEGER,TARGET_ID INTEGER,PROJECTION_KEY TEXT);"+
                "CREATE TABLE KingdomHistory(EVENT_ID INTEGER PRIMARY KEY,KINGDOM_ID INTEGER,WORLD_TIME REAL,YEAR_PREFIX TEXT,EVENT_TYPE TEXT,SUBJECT_NAME TEXT,CONTENT TEXT,CONTEXT_KINGDOM_ID INTEGER,TARGET_ID INTEGER,PROJECTION_KEY TEXT);"+
                "CREATE TABLE CityHistory(EVENT_ID INTEGER PRIMARY KEY,CITY_ID INTEGER,WORLD_TIME REAL,YEAR_PREFIX TEXT,EVENT_TYPE TEXT,SUBJECT_NAME TEXT,CONTENT TEXT,CONTEXT_KINGDOM_ID INTEGER,TARGET_ID INTEGER,PROJECTION_KEY TEXT);"+
                "CREATE TABLE CourtOfficer(OFFICER_ID INTEGER PRIMARY KEY,KINGDOM_ID INTEGER,ACTOR_ID INTEGER,CITY_ID INTEGER,COUNTY_ID INTEGER,LAYER TEXT,OFFICE_ID TEXT,ACTOR_NAME TEXT,APPOINTED_YEAR INTEGER,APPOINTED_TIME REAL,ENDED_YEAR INTEGER,ENDED_TIME REAL,ACTIVE INTEGER,END_REASON TEXT,RANK_AT_APPOINTMENT INTEGER,LOCAL_GRADE_AT_APPOINTMENT INTEGER);"+
                "CREATE TABLE DiplomacyProposal(PROPOSAL_ID INTEGER PRIMARY KEY,REQUESTER_KINGDOM_ID INTEGER,RESPONDER_KINGDOM_ID INTEGER,PROPOSAL_TYPE TEXT,STATUS TEXT,CREATED_YEAR INTEGER,CREATED_TIME REAL,REQUEST_YEAR_PREFIX TEXT,RESPONSE_YEAR INTEGER,RESPONSE_TIME REAL,RESPONSE_YEAR_PREFIX TEXT,RESPONSE_REASON TEXT)");
            action(db);
        } finally { AW3HistoryEventPublisher.Clear(); LineageArchiveManager.Instance.OperatingDB=null; LineageArchiveManager.RuntimeDatabaseEpoch++; File.Delete(path); }
    }
    private static void Exec(SQLiteConnection db,string sql) { using var command=new SQLiteCommand(sql,db);command.ExecuteNonQuery(); }
    private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
}
