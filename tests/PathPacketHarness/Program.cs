using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using NWO_RegionNode;
using NWO_RegionNode.PathValidation;
using System.Text;
using System.Reflection;
using System.Numerics;
using System.Security.Cryptography;
using System.IO;

var tests = new (string Name, Action Run)[]
{
    ("valid request response correlation", ValidCorrelation),
    ("zero user id is accepted by path packet decoder", ZeroUserIdAcceptedByDecoder),
    ("path validation does not require channel ownership binding", NoChannelOwnershipValidation),
    ("response fields round trip completely", CompleteResponse),
    ("malformed corridor and high index rejected", MalformedCorridors),
    ("maximum authoritative polygon index accepted", MaximumPolygonIndex),
    ("provider geometry failure propagated", GeometryFailure),
    ("fragmented frame", Fragmented),
    ("coalesced frames", Coalesced),
    ("coalesced toggle off and manual input preserve order", CoalescedOffManual),
    ("malformed body returns correlated invalid request", MalformedBody),
    ("v2 request codec and correlated response digest", V2CodecAndDigest),
    ("v2 route stores and steers exact wire waypoints", V2RouteExecution),
    ("optional Tokyo, Shanghai, and dateline route fixtures", OptionalRealRouteFixtures),
    ("too short advertised length rejected before append", () => BadLength(11)),
    ("too long advertised length rejected before append", () => BadLength(PathFrameDecoder.MAX_FRAME_LENGTH + 1)),
    ("oversized read rejected before storage expansion", OversizedRead),
    ("valid and invalid corridor regression", CorridorRegression),
    ("v2-only dateline corridor structural transition", DatelineCorridorRegression),
    ("land mask periodic double pixel lookup", LandMaskPeriodicLookup),
    ("long corridor samples distinct bounded segments", LongCorridorSampling),
    ("toggle handler uses object id without channel ownership binding", ToggleSafety),
    ("toggle request and ack exact byte layout", ToggleWireLayout),
    ("authenticated toggle drives production lockstep position toward route", RouteLockstep),
    ("toggle off resumes current-speed forward sailing without teleport", ToggleResumesOrdinarySailing),
    ("route anchors navmesh deltas to floating-origin ship position", FloatingOriginRoute),
    ("route completion clears speed and route", RouteCompletion),
    ("user metadata never overrides authoritative object id", ObjectIdIsAuthoritative),
    ("navigation adapter resolves ship id and writes correlated ack", NavigationAdapter),
    ("production startup pipeline routes framed path and toggle into lockstep snapshots", ProductionStartupPipeline)
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void ValidCorrelation()
{
    var ctx = new FakeContext();
    NWO_RegionNode.Program.userTable[7] = new User(ctx, 7, default, default, 0, 0);
    new PathPacketHandler(new HarnessProvider()).HandlePacket(ctx, Unpooled.WrappedBuffer(Request(0xdeadbeef)));
    var response = ParseResponse(ctx.TakeOne());
    Equal((byte)0x06, response.Opcode); Equal((byte)0x01, response.Subtype);
    Equal((ushort)1, response.ProtocolVersion); Equal((uint)response.RawLength, response.DeclaredLength);
    Equal((uint)0xdeadbeef, response.RequestId); Equal((ulong)0xdeadbeef, response.Correlation);
    Equal((byte)PathDecision.Accepted, response.Decision); Equal((ushort)PathReasonCode.VALID, response.Reason);
    Equal(HarnessProvider.Version, response.NavMeshVersion); Equal((uint)2, response.Sampled); Equal((uint)2, response.Total);
    True(!string.IsNullOrEmpty(response.ServerPlanId));
}

static void ToggleSafety()
{
    var decoder = new PathFrameDecoder(); var ctx = new FakeContext();
    byte[] frame = NavigationToggle(77, 55, true);
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(frame));
    Equal(1, ctx.Reads.Count); Equal((byte)2, ((IByteBuffer)ctx.Reads[0]).GetByte(1));

    var owner = new FakeContext(); var other = new FakeContext(new TestChannelId("intruder"));
    var user = new User(owner, 9, default, default, 0, 0) { scaffoldingIndex = 55 };
    NWO_RegionNode.Program.userTable[9] = user;
    var ship = new MoveMent(owner, 55, new MoveMent.nwo_Vector3(0, 0, 0), 5, 12) { targetspeed = 5, targetAngle = 12 };
    NWO_RegionNode.Program.moveMentTable[55] = ship;
    var provider = new CentroidProvider();
    var autopilot = new AutopilotController(provider);
    var handler = new NavigationFrameHandler(provider, autopilot);
    var secondHandler = new NavigationFrameHandler(new HarnessProvider(), autopilot);
    True(ReferenceEquals(autopilot, GetAutopilot(secondHandler)));
    True(autopilot.StoreValidatedRoute(9, 55, new[] { 1, 2 }, owner));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(RouteRequest(76, 9, 1, 2)));
    Equal((byte)PathDecision.Accepted, ParseResponse(owner.TakeOne()).Decision); True(autopilot.HasRoute(55));
    handler.ChannelRead(other, Unpooled.WrappedBuffer(RouteRequest(75, 9, 1, 2)));
    Equal((byte)PathDecision.Accepted, ParseResponse(other.TakeOne()).Decision); True(autopilot.HasRoute(55));
    handler.ChannelRead(other, Unpooled.WrappedBuffer(frame));
    Equal(100, ship.targetspeed); Equal((byte)193, ship.targetAngle);
    var acceptedOther = (IByteBuffer)other.Writes[^1]; CheckToggleAck(acceptedOther, 77, 55, true);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(frame));
    Equal(100, ship.targetspeed); Equal((byte)193, ship.targetAngle);
    var ack = (IByteBuffer)owner.Writes[^1]; CheckToggleAck(ack, 77, 55, true);
    var off = NavigationToggle(78, 55, false); handler.ChannelRead(owner, Unpooled.WrappedBuffer(off));
    Equal(5, ship.targetspeed); Equal(5f, ship.speed); Equal((uint)78, ((IByteBuffer)owner.Writes[^1]).GetUnsignedIntLE(8));
    True(autopilot.HasRoute(55));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(80, 55, true)));
    Equal(100, ship.targetspeed); True(autopilot.HasRoute(55));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(81, 55, false)));
    True(autopilot.HasRoute(55));
    var manual = new byte[11]; manual[0] = 0x04; manual[1] = 0x02; Put32(manual, 2, 55);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(manual));
    Equal(5, ship.targetspeed); True(autopilot.HasRoute(55));
    True(autopilot.StoreValidatedRoute(9, 55, new[] { 1, 2 }, owner)); var malformed = NavigationToggle(79, 55, true); Array.Resize(ref malformed, 18); Put32(malformed, 4, 18);
    var before = ship.targetAngle; handler.ChannelRead(owner, Unpooled.WrappedBuffer(malformed));
    Equal(5, ship.targetspeed); Equal(before, ship.targetAngle);
    NWO_RegionNode.Program.userTable.Remove(9); NWO_RegionNode.Program.moveMentTable.Remove(55);
}

static void NavigationAdapter()
{
    var owner = new FakeContext(); var other = new FakeContext(new TestChannelId("navigation-intruder"));
    const uint userId = 601, shipId = 602;
    NWO_RegionNode.Program.userTable[userId] = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = shipId };
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(0, 0, 0), 4, 12) { targetspeed = 4, targetAngle = 12 };
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var controller = new AutopilotController(new CentroidProvider());
    var first = new NavigationFrameHandler(new CentroidProvider(), controller);
    var second = new NavigationFrameHandler(new CentroidProvider(), controller);
    True(ReferenceEquals(controller, GetAutopilot(first)) && ReferenceEquals(controller, GetAutopilot(second)));
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));

    byte[] on = NavigationToggle(7001, shipId, true);
    first.ChannelRead(other, Unpooled.WrappedBuffer((byte[])on.Clone()));
    CheckToggleAck((IByteBuffer)other.Writes[^1], 7001, shipId, true);
    Equal(100, ship.targetspeed); True(ship.targetAngle >= 191 && ship.targetAngle <= 193);
    first.ChannelRead(owner, Unpooled.WrappedBuffer(on));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 7001, shipId, true);
    Equal(100, ship.targetspeed); True(ship.targetAngle >= 191 && ship.targetAngle <= 193);

    first.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(7002, shipId, false)));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 7002, shipId, true);
    Equal(4, ship.targetspeed); Equal(4f, ship.speed); True(controller.HasRoute(shipId));
    var manual = new byte[11]; manual[0] = 4; manual[1] = 2; Put32(manual, 2, shipId);
    first.ChannelRead(owner, Unpooled.WrappedBuffer(manual));
    Equal(4, ship.targetspeed); True(controller.HasRoute(shipId));
    first.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(7003, shipId, true)));
    Equal(100, ship.targetspeed); True(controller.HasRoute(shipId));

    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    first.ChannelInactive(owner);
    True(!controller.HasRoute(shipId));
    NWO_RegionNode.Program.userTable.Remove(userId); NWO_RegionNode.Program.moveMentTable.Remove(shipId);
}

static AutopilotController GetAutopilot(NavigationFrameHandler handler)
{
    var field = typeof(NavigationFrameHandler).GetField("_autopilot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    return (AutopilotController)field.GetValue(handler)!;
}

static object GetStoredRoute(AutopilotController controller, uint objectId)
{
    FieldInfo routesField = typeof(AutopilotController).GetField("_routes", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var routes = (System.Collections.IDictionary)routesField.GetValue(controller)!;
    return routes[objectId] ?? throw new Exception($"No stored route for objectId={objectId}");
}

static byte[] NavigationToggle(uint requestId, uint objectId, bool enabled, uint userId = 0)
{
    var frame = new byte[21]; frame[0] = 6; frame[1] = 2; frame[2] = 1;
    Put32(frame, 4, 21); Put32(frame, 8, requestId); Put32(frame, 12, userId); Put32(frame, 16, objectId); frame[20] = enabled ? (byte)1 : (byte)0;
    return frame;
}

static void CheckToggleAck(IByteBuffer ack, uint requestId, uint objectId, bool accepted, uint userId = 0)
{
    Equal(22, ack.ReadableBytes); Equal((byte)6, ack.GetByte(0)); Equal((byte)3, ack.GetByte(1));
    Equal((uint)22, ack.GetUnsignedIntLE(4)); Equal(requestId, ack.GetUnsignedIntLE(8)); Equal(userId, ack.GetUnsignedIntLE(12)); Equal(objectId, ack.GetUnsignedIntLE(16));
    Equal(accepted ? (byte)1 : (byte)0, ack.GetByte(20)); Equal(accepted ? (byte)0 : (byte)1, ack.GetByte(21));
}

static void ToggleWireLayout()
{
    var owner = new FakeContext();
    NWO_RegionNode.Program.userTable[91] = new User(owner, 91, default, default, 0, 0) { scaffoldingIndex = 92 };
    NWO_RegionNode.Program.moveMentTable[92] = new MoveMent(owner, 92, new MoveMent.nwo_Vector3(0, 0, 0), 0, 0);
    var wireController = new AutopilotController(new CentroidProvider());
    var handler = new NavigationFrameHandler(new CentroidProvider(), wireController);
    True(wireController.StoreValidatedRoute(91, 92, new[] { 1, 2 }, owner));
    var request = NavigationToggle(0xa1b2c3d4, 92, false, 91);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(request));
    var ack = (IByteBuffer)owner.Writes[^1];
    var actual = new byte[ack.ReadableBytes]; ack.GetBytes(ack.ReaderIndex, actual);
    byte[] expected = { 0x06, 0x03, 0x01, 0x00, 0x16, 0, 0, 0, 0xd4, 0xc3, 0xb2, 0xa1, 0x5b, 0, 0, 0, 0x5c, 0, 0, 0, 1, 0 };
    True(expected.SequenceEqual(actual));
    Equal((uint)0xa1b2c3d4, ack.GetUnsignedIntLE(8)); Equal((uint)91, ack.GetUnsignedIntLE(12)); Equal((uint)92, ack.GetUnsignedIntLE(16));
    NWO_RegionNode.Program.userTable.Remove(91); NWO_RegionNode.Program.moveMentTable.Remove(92);
}

static void RouteLockstep()
{
    var owner = new FakeContext();
    const uint userId = 101, shipId = 102;
    NWO_RegionNode.Program.userTable[userId] = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = shipId };
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(0, 0, 0), 0, 0);
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var provider = new CentroidProvider();
    var controller = new AutopilotController(provider);
    var handler = new NavigationFrameHandler(provider, controller);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(RouteRequest(990, userId, 1, 2)));
    var routeAck = ParseResponse(owner.TakeOne());
    Equal((uint)990, routeAck.RequestId); Equal((ulong)990, routeAck.Correlation); Equal((byte)PathDecision.Accepted, routeAck.Decision);
    Equal(0, ship.targetspeed);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(1001, shipId, true)));
    var ack = (IByteBuffer)owner.Writes[^1];
    CheckToggleAck(ack, 1001, shipId, true);
    owner.Writes.RemoveAt(owner.Writes.Count - 1);
    double startDistance = DistanceToDestination(ship, provider, 2);
    for (int tick = 0; tick < 55; tick++)
        LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(ship.globalPosition.X > 0);
    True(DistanceToDestination(ship, provider, 2) < startDistance);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(1002, shipId, false)));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 1002, shipId, true);
    owner.Writes.RemoveAt(owner.Writes.Count - 1);
    Equal((int)ship.speed, ship.targetspeed);
    int resumedAt = ship.globalPosition.X;
    for (int tick = 0; tick < 12; tick++)
        LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(ship.globalPosition.X != resumedAt);
    Equal((int)ship.speed, ship.targetspeed);
    True(controller.HasRoute(shipId));
    var invalidRoute = RouteRequest(991, userId, 1, int.MaxValue);
    ship.targetspeed = 6;
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(invalidRoute));
    var invalidAck = ParseResponse(owner.TakeOne());
    Equal((uint)991, invalidAck.RequestId); Equal((byte)PathDecision.Rejected, invalidAck.Decision);
    Equal(6, ship.targetspeed);
    True(controller.HasRoute(shipId));
    double resumeDistance = DistanceToDestination(ship, provider, 2);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(1003, shipId, true)));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 1003, shipId, true);
    for (int tick = 0; tick < 10; tick++)
        LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(DistanceToDestination(ship, provider, 2) < resumeDistance);
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(1004, shipId, true)));
    var manual = new byte[11]; manual[0] = 0x04; manual[1] = 0x02; Put32(manual, 2, shipId);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(manual));
    int manualStarted = ship.globalPosition.X; LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(ship.globalPosition.X != manualStarted || ship.globalPosition.Z != 0); Equal((int)ship.speed, ship.targetspeed); True(controller.HasRoute(shipId));
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(1005, shipId, true)));
    handler.ChannelInactive(owner);
    int disconnectedStarted = ship.globalPosition.X; LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(ship.globalPosition.X != disconnectedStarted || ship.globalPosition.Z != 0); True(ship.speed >= 0); True(!controller.HasRoute(shipId));
    NWO_RegionNode.Program.userTable.Remove(userId); NWO_RegionNode.Program.moveMentTable.Remove(shipId);
}

static void ToggleResumesOrdinarySailing()
{
    var owner = new FakeContext(); const uint userId = 1009, shipId = 1010;
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(1200, 0, -3400), 6, 0) { targetspeed = 6, targetAngle = 0, rot = 37 };
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var controller = new AutopilotController(new CentroidProvider());
    var handler = new NavigationFrameHandler(new CentroidProvider(), controller);
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(1, shipId, true, userId)));
    Equal(100, ship.targetspeed);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(2, shipId, false, userId)));
    Equal(6, ship.targetspeed); Equal(6f, ship.speed); Equal(ship.rot, ship.targetAngle);
    int beforeX = ship.globalPosition.X, beforeZ = ship.globalPosition.Z;
    LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    Equal((byte)37, ship.rot);
    True(ship.globalPosition.X != beforeX || ship.globalPosition.Z != beforeZ);
    True(Math.Abs(ship.globalPosition.X - beforeX) < 100 && Math.Abs(ship.globalPosition.Z - beforeZ) < 100);
    True(ship.globalPosition.X != beforeX || ship.globalPosition.Z != beforeZ);

    // A ship with no prior motion remains safely stopped when navigation is disabled.
    var idle = new MoveMent(owner, shipId + 1, new MoveMent.nwo_Vector3(0, 0, 0), 0, 0);
    NWO_RegionNode.Program.moveMentTable[idle.id] = idle;
    controller.Pause(idle.id);
    Equal(0, idle.targetspeed); Equal(0f, idle.speed);
    NWO_RegionNode.Program.moveMentTable.Remove(shipId); NWO_RegionNode.Program.moveMentTable.Remove(idle.id);
}

static double DistanceToDestination(MoveMent ship, IAuthoritativeNavMeshProvider provider, int polygon)
{
    True(provider.TryGetCentroid(polygon, out double x, out double y));
    double dx = x * 1280.0 - ship.globalPosition.X, dz = -y * 1280.0 - ship.globalPosition.Z;
    return Math.Sqrt(dx * dx + dz * dz);
}

static void RouteCompletion()
{
    var owner = new FakeContext(); const uint userId = 111, shipId = 112;
    NWO_RegionNode.Program.userTable[userId] = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = shipId };
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(0, 0, 0), 0, 0) { targetspeed = 8, speed = 4 };
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var provider = new AdjustableCentroidProvider(); var controller = new AutopilotController(provider);
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    True(controller.Toggle(userId, shipId, 1, true, owner));
    provider.DestinationX = 0.1;
    LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(!controller.HasRoute(shipId)); Equal((int)ship.speed, ship.targetspeed);
    int x = ship.globalPosition.X; LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0); True(ship.globalPosition.X != x || ship.globalPosition.Z != 0);
    NWO_RegionNode.Program.userTable.Remove(userId); NWO_RegionNode.Program.moveMentTable.Remove(shipId);
}

static void FloatingOriginRoute()
{
    var owner = new FakeContext(); const uint userId = 113, shipId = 114;
    const int originX = 250000, originZ = -91000;
    NWO_RegionNode.Program.userTable[userId] = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = shipId };
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(originX, 0, originZ), 0, 0);
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var provider = new CentroidProvider(); var controller = new AutopilotController(provider);
    // Navmesh start coordinates (0,0) intentionally differ from the server's
    // floating-origin position. The route must store and steer by relative deltas.
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    True(controller.Toggle(userId, shipId, 1, true, owner));
    for (int tick = 0; tick < 55; tick++) LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
    True(ship.globalPosition.X > originX);
    double traveled = Math.Sqrt(Math.Pow(ship.globalPosition.X - originX, 2) + Math.Pow(ship.globalPosition.Z - originZ, 2));
    True(traveled < 500);
    NWO_RegionNode.Program.userTable.Remove(userId); NWO_RegionNode.Program.moveMentTable.Remove(shipId);
}

static void ProductionStartupPipeline()
{
    string root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "NWO_RegionNode"));
    var resolution = NWO_RegionNode.Program.InitializeNavigation(root);
    if (resolution.FailClosed || resolution.Provider is not AuthoritativeNavMeshProvider)
        throw new Exception($"Production provider did not resolve: source={resolution.Source}; detail={resolution.Detail}");
    var autopilot = NWO_RegionNode.Program.Autopilot!;
    var channelId = new TestChannelId("pipeline-owner");
    var pipelineChannel = new DotNetty.Transport.Channels.Embedded.EmbeddedChannel(channelId);
    NWO_RegionNode.Program.ConfigureClientPipeline(pipelineChannel.Pipeline, resolution.Provider, autopilot);
    Equal("HeadContext,PathFrameDecoder,NavigationFrameHandler,EchoServerHandler,TailContext", string.Join(",", pipelineChannel.Pipeline.Select(h => h.GetType().Name)));

    const uint userId = 1201, shipId = 1202;
    var owner = new ChannelContext(pipelineChannel);
    var user = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = shipId };
    NWO_RegionNode.Program.userTable[userId] = user;
    string meshFile = Path.Combine(root, "data", NavMeshProviderFactory.NAVMESH_FILE_NAME);
    using var meshJson = System.Text.Json.JsonDocument.Parse(File.OpenRead(meshFile));
    var portal = meshJson.RootElement.GetProperty("portals")[0];
    int startPolygon = portal[0].GetInt32(), destinationPolygon = portal[1].GetInt32();
    True(resolution.Provider.CanMoveBetween(startPolygon, destinationPolygon));
    True(resolution.Provider.TryGetCentroid(startPolygon, out var startX, out var startY));
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3((int)(startX * 1280), 0, (int)(-startY * 1280)), 0, 0);
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;

    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(RouteRequest(0x3141, userId, startPolygon, destinationPolygon)));
    var routeAck = ParseResponse(ReadOutboundBuffer(pipelineChannel));
    Equal((uint)0x3141, routeAck.RequestId);
    if (routeAck.Decision != (byte)PathDecision.Accepted) throw new Exception($"Configured route rejected: decision={routeAck.Decision}; reason={routeAck.Reason}; start=({startX},{startY})");
    True(autopilot.HasRoute(shipId));
    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3142, shipId, true)));
    CheckToggleAck(ReadOutboundBuffer(pipelineChannel), 0x3142, shipId, true);

    var snapshots = new List<int>();
    for (int tick = 0; tick < 12; tick++)
    {
        NWO_RegionNode.Program.AdvanceAndBroadcastMovement(autopilot, NWO_RegionNode.Program.moveMentTable.Values, static (_, _) => 0, NWO_RegionNode.Program.userTable.Values, NWO_RegionNode.Program.SendMovementSnapshot);
        IByteBuffer snapshot = ReadOutboundBuffer(pipelineChannel);
        Equal(25, snapshot.ReadableBytes); Equal((byte)0x04, snapshot.GetByte(0)); Equal((byte)0x01, snapshot.GetByte(1));
        Equal(userId, snapshot.GetUnsignedIntLE(2)); Equal((ushort)1, snapshot.GetUnsignedShortLE(6)); Equal(shipId, snapshot.GetUnsignedIntLE(8));
        snapshots.Add(snapshot.GetIntLE(12)); snapshot.Release();
    }
    True(snapshots.Distinct().Count() > 1); True(snapshots[^1] != snapshots[0]);

    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3143, shipId, false)));
    CheckToggleAck(ReadOutboundBuffer(pipelineChannel), 0x3143, shipId, true);
    AssertTicksAdvance(pipelineChannel, autopilot, ship, snapshots[^1]);

    True(autopilot.StoreValidatedRoute(userId, shipId, new[] { startPolygon, destinationPolygon }, owner));
    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3144, shipId, true))); ReadOutboundBuffer(pipelineChannel).Release();
    byte[] manual = new byte[PathFrameDecoder.MANUAL_MOVEMENT_LENGTH]; manual[0] = 4; manual[1] = 2; Put32(manual, 2, shipId);
    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(manual)); AssertTicksAdvance(pipelineChannel, autopilot, ship, ship.globalPosition.X);

    True(autopilot.StoreValidatedRoute(userId, shipId, new[] { startPolygon, destinationPolygon }, owner));
    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3145, shipId, true))); ReadOutboundBuffer(pipelineChannel).Release();
    pipelineChannel.CloseAsync().GetAwaiter().GetResult(); AssertTicksAdvance(pipelineChannel, autopilot, ship, ship.globalPosition.X, expectPackets: false);

    True(autopilot.StoreValidatedRoute(userId, shipId, new[] { startPolygon, destinationPolygon }, owner));
    // Channel close above verifies disconnect cleanup. Use a second actual configured pipeline for completion.
    pipelineChannel = new DotNetty.Transport.Channels.Embedded.EmbeddedChannel(channelId = new TestChannelId("pipeline-owner-completion"));
    NWO_RegionNode.Program.ConfigureClientPipeline(pipelineChannel.Pipeline, resolution.Provider, autopilot);
    user.IChannel = new ChannelContext(pipelineChannel);
    pipelineChannel.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3146, shipId, true))); ReadOutboundBuffer(pipelineChannel).Release();
    True(resolution.Provider.TryGetCentroid(destinationPolygon, out var destinationX, out var destinationY));
    ship.globalPosition = new MoveMent.nwo_Vector3((int)(destinationX * 1280), 0, (int)(-destinationY * 1280));
    NWO_RegionNode.Program.AdvanceAndBroadcastMovement(autopilot, new[] { ship }, static (_, _) => 0, NWO_RegionNode.Program.userTable.Values, NWO_RegionNode.Program.SendMovementSnapshot);
    ReadOutboundBuffer(pipelineChannel).Release();
    True(!autopilot.HasRoute(shipId)); Equal((int)ship.speed, ship.targetspeed);
    int completedX = ship.globalPosition.X; AssertTicksAdvance(pipelineChannel, autopilot, ship, completedX);

    // A foreign channel and a malformed request cannot mutate ship controls or route state.
    ship.targetspeed = 7; ship.targetAngle = 33;
    var stranger = new DotNetty.Transport.Channels.Embedded.EmbeddedChannel();
    NWO_RegionNode.Program.ConfigureClientPipeline(stranger.Pipeline, resolution.Provider, autopilot);
    stranger.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3147, shipId, true)));
    CheckToggleAck(ReadOutboundBuffer(stranger), 0x3147, shipId, false);
    var malformedChannel = new DotNetty.Transport.Channels.Embedded.EmbeddedChannel();
    NWO_RegionNode.Program.ConfigureClientPipeline(malformedChannel.Pipeline, resolution.Provider, autopilot);
    malformedChannel.WriteInbound(Unpooled.WrappedBuffer(NavigationToggle(0x3148, shipId, true).Concat(new byte[] { 0 }).ToArray()));
    True(NWO_RegionNode.Program.userTable.ContainsKey(userId));
    Equal(7, ship.targetspeed); Equal((byte)33, ship.targetAngle);
    NWO_RegionNode.Program.userTable.Remove(userId); NWO_RegionNode.Program.moveMentTable.Remove(shipId);
    pipelineChannel.FinishAndReleaseAll(); stranger.FinishAndReleaseAll(); malformedChannel.FinishAndReleaseAll();
}

static IByteBuffer ReadOutboundBuffer(DotNetty.Transport.Channels.Embedded.EmbeddedChannel channel)
    => channel.ReadOutbound<IByteBuffer>() ?? throw new Exception("Expected outbound buffer from configured pipeline");

static void Feed(PathFrameDecoder decoder, NavigationFrameHandler navigation, FakeContext context, byte[] bytes)
{
    decoder.ChannelRead(context, Unpooled.WrappedBuffer(bytes));
    foreach (IByteBuffer frame in context.Reads.ToArray()) navigation.ChannelRead(context, frame);
    context.Reads.Clear();
}

static void AssertTicksHalted(DotNetty.Transport.Channels.Embedded.EmbeddedChannel channel, AutopilotController autopilot, MoveMent ship, int x, bool expectPackets = true)
{
    for (int tick = 0; tick < 4; tick++)
    {
        NWO_RegionNode.Program.AdvanceAndBroadcastMovement(autopilot, new[] { ship }, static (_, _) => 0, NWO_RegionNode.Program.userTable.Values, NWO_RegionNode.Program.SendMovementSnapshot);
        if (expectPackets)
        {
            IByteBuffer snapshot = ReadOutboundBuffer(channel);
            Equal(25, snapshot.ReadableBytes); Equal((byte)0x04, snapshot.GetByte(0)); Equal((byte)0x01, snapshot.GetByte(1));
            Equal(ProgramUserId(ship), snapshot.GetUnsignedIntLE(2)); Equal((ushort)1, snapshot.GetUnsignedShortLE(6)); Equal(ship.id, snapshot.GetUnsignedIntLE(8));
            Equal(x, snapshot.GetIntLE(12)); snapshot.Release();
        }
    }
    Equal(x, ship.globalPosition.X); Equal(0, ship.targetspeed); Equal(0, ship.speed); True(!autopilot.HasRoute(ProgramUserId(ship)));
}

static void AssertTicksAdvance(DotNetty.Transport.Channels.Embedded.EmbeddedChannel channel, AutopilotController autopilot, MoveMent ship, int x, bool expectPackets = true)
{
    for (int tick = 0; tick < 2; tick++)
    {
        NWO_RegionNode.Program.AdvanceAndBroadcastMovement(autopilot, new[] { ship }, static (_, _) => 0, NWO_RegionNode.Program.userTable.Values, NWO_RegionNode.Program.SendMovementSnapshot);
        if (expectPackets) ReadOutboundBuffer(channel).Release();
    }
    True(ship.globalPosition.X != x || ship.globalPosition.Z != 0);
    Equal((int)ship.speed, ship.targetspeed);
}

static uint ProgramUserId(MoveMent ship) => NWO_RegionNode.Program.userTable.Single(p => p.Value.scaffoldingIndex == ship.id).Key;

static void NoChannelOwnershipValidation()
{
    var ownerId = new TestChannelId("owner");
    var owner = new FakeContext(ownerId); NWO_RegionNode.Program.userTable[7] = new User(owner, 7, default, default, 0, 0);
    var other = new FakeContext(new TestChannelId("other"));
    new PathPacketHandler(new HarnessProvider()).HandlePacket(other, Unpooled.WrappedBuffer(Request(31337)));
    Equal((byte)PathDecision.Accepted, ParseResponse(other.TakeOne()).Decision);
    var unknown = new FakeContext();
    new PathPacketHandler(new HarnessProvider()).HandlePacket(unknown, Unpooled.WrappedBuffer(Request(31338, 999)));
    Equal((byte)PathDecision.Accepted, ParseResponse(unknown.TakeOne()).Decision);
    var zero = new FakeContext();
    new PathPacketHandler(new HarnessProvider()).HandlePacket(zero, Unpooled.WrappedBuffer(Request(31340, 0)));
    Equal((byte)PathDecision.Accepted, ParseResponse(zero.TakeOne()).Decision);
    new PathPacketHandler(new HarnessProvider()).HandlePacket(owner, Unpooled.WrappedBuffer(Request(31339)));
    Equal((byte)PathDecision.Accepted, ParseResponse(owner.TakeOne()).Decision);
}

static void CompleteResponse()
{
    var result = new PathValidationResult { Decision = (byte)PathDecision.Limited, ReasonCode = 0x1234,
        SampledSegments = 64, TotalSegments = 4095, ServerPlanId = "sp1_0123456789abcdef" };
    var packet = PathPolygonDecoder.EncodeResponse(result, 0x89abcdef, "mesh-version-hash");
    var parsed = ParseResponse(Unpooled.WrappedBuffer(packet));
    Equal((uint)0x89abcdef, parsed.RequestId); Equal((ulong)0x89abcdef, parsed.Correlation);
    Equal((byte)PathDecision.Limited, parsed.Decision); Equal((ushort)0x1234, parsed.Reason);
    Equal("mesh-version-hash", parsed.NavMeshVersion); Equal((uint)64, parsed.Sampled); Equal((uint)4095, parsed.Total);
    Equal("sp1_0123456789abcdef", parsed.ServerPlanId);
}

static void MalformedCorridors()
{
    var ctx = new FakeContext(); NWO_RegionNode.Program.userTable[7] = new User(ctx, 7, default, default, 0, 0);
    var handler = new PathPacketHandler(new HarnessProvider());
    var trailing = Request(8101); Array.Resize(ref trailing, trailing.Length + 1); Put32(trailing, 4, (uint)trailing.Length);
    handler.HandlePacket(ctx, Unpooled.WrappedBuffer(trailing));
    Equal((ushort)PathReasonCode.INVALID_REQUEST, ParseResponse(ctx.TakeOne()).Reason);
    handler.HandlePacket(ctx, Unpooled.WrappedBuffer(RequestPath(8102, 7, 1, 1, 3)));
    Equal((ushort)PathReasonCode.PATH_DISCONTINUITY, ParseResponse(ctx.TakeOne()).Reason);
    handler.HandlePacket(ctx, Unpooled.WrappedBuffer(RequestPath(8103, 7, 1, int.MaxValue, 3)));
    Equal((ushort)PathReasonCode.PLAN_INVALID, ParseResponse(ctx.TakeOne()).Reason);
}

static void ZeroUserIdAcceptedByDecoder()
{
    byte[] raw = RouteRequestForObject(8100, 0, 0, 1, 2, 3);
    Equal(0u, Get32(raw, 12)); Equal(0u, Get32(raw, 16));
    Equal(3u, Get32(raw, 20)); Equal(1, unchecked((int)Get32(raw, 24)));
    var packet = Unpooled.WrappedBuffer(raw);
    True(PathPolygonDecoder.TryDecode(packet, packet.ReaderIndex, out var request, out var error));
    True(error == PathReasonCode.VALID);
    Equal(0u, request.UserId);
    Equal(0u, request.ObjectId);
}

static void ObjectIdIsAuthoritative()
{
    var owner = new FakeContext(); const uint userId = 0, decoyObjectId = 301, objectId = 302;
    NWO_RegionNode.Program.userTable[userId] = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = decoyObjectId };
    var decoy = new MoveMent(owner, decoyObjectId, new MoveMent.nwo_Vector3(0, 0, 0), 0, 0);
    var selected = new MoveMent(owner, objectId, new MoveMent.nwo_Vector3(0, 0, 0), 0, 0);
    NWO_RegionNode.Program.moveMentTable[decoyObjectId] = decoy;
    NWO_RegionNode.Program.moveMentTable[objectId] = selected;
    var provider = new CentroidProvider(); var controller = new AutopilotController(provider);
    var handler = new NavigationFrameHandler(provider, controller);

    byte[] route = RouteRequestForObject(0x5151, userId, objectId, 1, 2);
    Equal(userId, Get32(route, 12)); Equal(objectId, Get32(route, 16));
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(route));
    Equal((byte)PathDecision.Accepted, ParseResponse(owner.TakeOne()).Decision);
    True(controller.HasRoute(objectId)); True(!controller.HasRoute(decoyObjectId));

    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(0x5152, objectId, true, userId)));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 0x5152, objectId, true, userId);
    for (int tick = 0; tick < 20; tick++) LockstepMovement.AdvanceTick(controller, new[] { decoy, selected }, static (_, _) => 0);
    True(selected.globalPosition.X > 0); Equal(0, decoy.globalPosition.X);

    NWO_RegionNode.Program.userTable.Remove(userId);
    NWO_RegionNode.Program.moveMentTable.Remove(decoyObjectId); NWO_RegionNode.Program.moveMentTable.Remove(objectId);
}

static void MaximumPolygonIndex()
{
    const int max = AuthoritativeNavMeshProvider.ExpectedPolygonCount - 1;
    var validator = new PathValidator(new RangeProvider(max));
    var request = new PathPolygonIndexRequest { RequestId = 1, StartPolygonIndex = max - 1, DestinationPolygonIndex = max,
        PathPolygonCount = 2, PathPolygonIndices = new[] { max - 1, max } };
    Equal((ushort)PathReasonCode.VALID, validator.Validate(request).ReasonCode);
}

static void GeometryFailure()
{
    var ctx = new FakeContext();
    NWO_RegionNode.Program.userTable[7] = new User(ctx, 7, default, default, 0, 0);
    new PathPacketHandler(new FailingGeometryProvider()).HandlePacket(ctx, Unpooled.WrappedBuffer(Request(4242)));
    var response = ParseResponse(ctx.TakeOne());
    Equal((byte)PathDecision.Rejected, response.Decision);
    Equal((ushort)PathReasonCode.OFF_MESH_SAMPLE, response.Reason);
    Equal((ulong)4242, response.Correlation);
    Equal((uint)4242, response.RequestId);
}

static void Fragmented()
{
    var decoder = new PathFrameDecoder(); var ctx = new FakeContext(); var packet = Request(101);
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(packet[..7]));
    Equal(7, decoder.PendingBytes);
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(packet[7..]));
    Equal(0, decoder.PendingBytes); Equal(1, ctx.Reads.Count);
    var handler = new PathPacketHandler(new HarnessProvider()); handler.HandlePacket(ctx, (IByteBuffer)ctx.Reads[0]);
    Equal((uint)101, ParseResponse(ctx.TakeOne()).RequestId);
}

static void Coalesced()
{
    var decoder = new PathFrameDecoder(); var ctx = new FakeContext();
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(Request(1).Concat(Request(2)).ToArray()));
    Equal(2, ctx.Reads.Count); Equal(0, decoder.PendingBytes);
}

static void CoalescedOffManual()
{
    var owner = new FakeContext(); const uint userId = 801, shipId = 802;
    NWO_RegionNode.Program.userTable[userId] = new User(owner, userId, default, default, 0, 0) { scaffoldingIndex = shipId };
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(0, 0, 0), 8, 9) { targetspeed = 20, targetAngle = 9 };
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var controller = new AutopilotController(new CentroidProvider());
    True(controller.StoreValidatedRoute(userId, shipId, new[] { 1, 2 }, owner));
    var decoder = new PathFrameDecoder(); var navigation = new NavigationFrameHandler(new CentroidProvider(), controller);
    var ctx = new FakeContext();
    var off = NavigationToggle(9001, shipId, false);
    var manual = new byte[11]; manual[0] = 4; manual[1] = 2; Put32(manual, 2, shipId);
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(off.Concat(manual).ToArray()));
    Equal(2, ctx.Reads.Count);
    navigation.ChannelRead(owner, (IByteBuffer)ctx.Reads[0]);
    Equal(8, ship.targetspeed); Equal(8f, ship.speed); True(controller.HasRoute(shipId));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 9001, shipId, true);
    navigation.ChannelRead(owner, (IByteBuffer)ctx.Reads[1]);
    True(controller.HasRoute(shipId));
    Equal(11, ((IByteBuffer)ctx.Reads[1]).ReadableBytes);
    Equal(8, ship.targetspeed); // legacy 0x04/0x02 remains a synchronization packet here.
    NWO_RegionNode.Program.userTable.Remove(userId); NWO_RegionNode.Program.moveMentTable.Remove(shipId);
}

static void MalformedBody()
{
    var packet = Request(777); packet[2] = 0; packet[3] = 0; // invalid protocol version
    var ctx = new FakeContext(); var input = Unpooled.WrappedBuffer(packet); input.SetReaderIndex(0); new PathPacketHandler(new HarnessProvider()).HandlePacket(ctx, input);
    var raw = ctx.TakeOne(); var response = ParseResponse(raw); Equal((uint)777, response.RequestId);
    if (response.Reason != (ushort)PathReasonCode.INVALID_REQUEST) throw new Exception($"invalid body reason {response.Reason}");
}

static void V2CodecAndDigest()
{
    byte[] raw = RouteRequestV2(0x22334455, 7, 55, new[] { 1, 2 },
        new[] { new Vector2(100, 100), new Vector2(105, 105) });
    var input = Unpooled.WrappedBuffer(raw);
    True(PathPolygonDecoder.TryDecode(input, input.ReaderIndex, out var request, out var error));
    Equal(PathReasonCode.VALID, error); Equal((ushort)2, request.ProtocolVersion);
    Equal(7u, request.UserId); Equal(55u, request.ObjectId); Equal(HarnessProvider.Version, request.NavMeshVersion);
    Equal(1280f, request.NavScale); Equal(2048f, request.MapSize); Equal(1u, request.CoordinateFlags);
    Equal(2, request.PathPolygonIndices.Length); Equal(2, request.Waypoints.Length);
    Equal(BitConverter.SingleToInt32Bits(105f), BitConverter.SingleToInt32Bits(request.Waypoints[1].X));
    byte[] digest = SHA256.HashData(raw.AsSpan(20));
    True(digest.SequenceEqual(request.GeometryHash));

    var provider = new V2GeometryProvider(); var ctx = new FakeContext();
    new PathPacketHandler(provider).HandlePacket(ctx, Unpooled.WrappedBuffer(raw), routeAcceptedV2: _ => true);
    var response = ParseResponse(ctx.TakeOne());
    Equal((ushort)2, response.ProtocolVersion); Equal(0x22334455u, response.RequestId);
    Equal((ulong)0x22334455, response.Correlation); Equal((byte)PathDecision.Accepted, response.Decision);
    True(digest.SequenceEqual(response.GeometryHash));
    True(provider.SawNonCentroidWaypoint);

    var noV2Callback = new FakeContext();
    new PathPacketHandler(provider).HandlePacket(noV2Callback, Unpooled.WrappedBuffer(raw),
        routeAccepted: (_, _, _) => true);
    var rejected = ParseResponse(noV2Callback.TakeOne());
    Equal((ushort)2, rejected.ProtocolVersion); Equal((byte)PathDecision.Rejected, rejected.Decision);
    True(rejected.GeometryHash.All(static value => value == 0));

    byte[] wrongVersion = (byte[])raw.Clone(); wrongVersion[2] = 3;
    True(!PathPolygonDecoder.TryDecode(Unpooled.WrappedBuffer(wrongVersion), 0, out _, out _));
    byte[] trailing = raw.Concat(new byte[] { 0 }).ToArray(); Put32(trailing, 4, (uint)trailing.Length);
    True(!PathPolygonDecoder.TryDecode(Unpooled.WrappedBuffer(trailing), 0, out _, out _));
    byte[] wrongScale = (byte[])raw.Clone(); Put32(wrongScale, 52, (uint)BitConverter.SingleToInt32Bits(1f));
    True(!PathPolygonDecoder.TryDecode(Unpooled.WrappedBuffer(wrongScale), 0, out _, out _));
    byte[] wrongFlags = (byte[])raw.Clone(); Put32(wrongFlags, 60, 3);
    True(!PathPolygonDecoder.TryDecode(Unpooled.WrappedBuffer(wrongFlags), 0, out _, out _));
    byte[] nonfinite = (byte[])raw.Clone(); Put32(nonfinite, 84, 0x7f800000);
    True(!PathPolygonDecoder.TryDecode(Unpooled.WrappedBuffer(nonfinite), 0, out _, out _));
}

static void V2RouteExecution()
{
    var owner = new FakeContext(); const uint userId = 708, shipId = 709;
    var ship = new MoveMent(owner, shipId, new MoveMent.nwo_Vector3(1000, 0, 2000), 0, 12);
    NWO_RegionNode.Program.moveMentTable[shipId] = ship;
    var provider = new V2GeometryProvider(); var controller = new AutopilotController(provider);
    var handler = new NavigationFrameHandler(provider, controller);
    byte[] route = RouteRequestV2(0x7711, userId, shipId, new[] { 1, 2 },
        new[] { new Vector2(100, 100), new Vector2(105, 105), new Vector2(106, 100) });
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(route));
    var accepted = ParseResponse(owner.TakeOne());
    Equal((ushort)2, accepted.ProtocolVersion); Equal((byte)PathDecision.Accepted, accepted.Decision);
    True(controller.HasRoute(shipId)); True(provider.SawNonCentroidWaypoint);
    handler.ChannelRead(owner, Unpooled.WrappedBuffer(NavigationToggle(0x7712, shipId, true, userId)));
    CheckToggleAck((IByteBuffer)owner.Writes[^1], 0x7712, shipId, true, userId);
    // Toggle activates V2 without an immediate steering or position change.
    Equal((byte)12, ship.targetAngle);
    Equal(0, ship.targetspeed);
    NWO_RegionNode.Program.moveMentTable.Remove(shipId);
}

static void OptionalRealRouteFixtures()
{
    string[] names = { "NWO_RouteV2_Tokyo.bin", "NWO_RouteV2_Shanghai.bin", "NWO_RouteV2_Dateline.bin", "NWO_RouteV2_DatelineReverse.bin" };
    bool requireFixtures = Environment.GetCommandLineArgs().Contains("--require-route-v2-fixtures", StringComparer.Ordinal);
    var missing = names.Where(name => !File.Exists(Path.Combine("C:\\Temp", name))).ToArray();
    if (requireFixtures && missing.Length != 0)
        throw new FileNotFoundException("Required real route fixtures are missing: " + string.Join(", ", missing));

    var evidence = new List<object>();
    var fixtureProvider = AuthoritativeNavMeshProvider.Load(Path.Combine(AppContext.BaseDirectory, "data", NavMeshProviderFactory.NAVMESH_FILE_NAME));
    foreach (string name in names)
    {
        string path = Path.Combine("C:\\Temp", name);
        if (!File.Exists(path)) continue;
        byte[] raw = File.ReadAllBytes(path);
        var input = Unpooled.WrappedBuffer(raw);
        bool decoded = PathPolygonDecoder.TryDecode(input, input.ReaderIndex, out var request, out var error);
        input.Release();
        if (!decoded)
        {
            string fixtureMeshHash = Convert.ToHexString(raw.AsSpan(20, 32)).ToLowerInvariant();
            if (!string.Equals(fixtureMeshHash, AuthoritativeNavMeshProvider.TrustedVersion, StringComparison.Ordinal))
                throw new InvalidDataException($"Fixture {name} has mesh hash {fixtureMeshHash}, but the decoder/provider pin is {AuthoritativeNavMeshProvider.TrustedVersion}; current fixture pin mismatch is pending external mesh acceptance.");
            throw new InvalidDataException($"Fixture {name} failed packet decoding: {error}.");
        }
        Equal(PathReasonCode.VALID, error); Equal((ushort)2, request.ProtocolVersion);
        True(SHA256.HashData(raw.AsSpan(20)).SequenceEqual(request.GeometryHash));
        True(request.Waypoints.Length >= 2 && request.PathPolygonIndices.Length >= 2);
        // Keep a deterministic parity hook: every decoded float must match its exact
        // little-endian wire bits before the trusted provider validates the route.
        for (int i = 0; i < request.Waypoints.Length; i++)
        {
            int offset = checked(72 + request.PathPolygonIndices.Length * 4 + i * 8);
            Equal(Get32(raw, offset), (uint)BitConverter.SingleToInt32Bits(request.Waypoints[i].X));
            Equal(Get32(raw, offset + 4), (uint)BitConverter.SingleToInt32Bits(request.Waypoints[i].Y));
        }
        var provider = fixtureProvider;
        Equal(AuthoritativeNavMeshProvider.TrustedVersion, provider.GetVersion());
        ushort validationReason = new PathValidator(provider).Validate(request).ReasonCode;
        if (validationReason != (ushort)PathReasonCode.VALID)
            throw new InvalidDataException($"Fixture {name} validation rejected with reason {validationReason}; fixture mesh asset hash is {request.NavMeshVersion}, packaged provider hash is {provider.GetVersion()}.");

        var navigation = new NavigationFrameHandler(provider, new AutopilotController(provider));
        var channel = new DotNetty.Transport.Channels.Embedded.EmbeddedChannel(new PathFrameDecoder(), navigation);
        var controller = GetAutopilot(navigation);
        var owner = new ChannelContext(channel);
        bool hadShip = NWO_RegionNode.Program.moveMentTable.TryGetValue(request.ObjectId, out MoveMent priorShip);
        var ship = new MoveMent(owner, request.ObjectId,
            new MoveMent.nwo_Vector3(123456, 0, -654321), 0, 17);
        NWO_RegionNode.Program.moveMentTable[request.ObjectId] = ship;
        try
        {
            // The production decoder/handler validates and stores from this exact packet.
            channel.WriteInbound(Unpooled.WrappedBuffer(raw));
            IByteBuffer responseBuffer = ReadOutboundBuffer(channel);
            byte[] responseBytes = new byte[responseBuffer.ReadableBytes];
            responseBuffer.GetBytes(responseBuffer.ReaderIndex, responseBytes, 0, responseBytes.Length);
            var response = ParseResponse(responseBuffer);
            byte[] requestDigest = SHA256.HashData(raw.AsSpan(20));
            Equal((ushort)2, response.ProtocolVersion);
            Equal(request.RequestId, response.RequestId);
            Equal((ulong)request.RequestId, response.Correlation);
            Equal(provider.GetVersion(), response.NavMeshVersion);
            Equal((byte)PathDecision.Accepted, response.Decision);
            True(requestDigest.SequenceEqual(response.GeometryHash));
            True(controller.HasRoute(request.ObjectId));

            int firstPointOffset = checked(72 + request.PathPolygonIndices.Length * 4);
            raw[firstPointOffset] ^= 0x01; // Mutating the consumed input cannot mutate accepted route state.

            string responsePath = Path.Combine("C:\\Temp", Path.GetFileNameWithoutExtension(name) + ".response.bin");
            File.WriteAllBytes(responsePath, responseBytes);

            object route = GetStoredRoute(controller, request.ObjectId);
            Type routeType = route.GetType();
            var storedPoints = (Vector2[])routeType.GetField("Waypoints")!.GetValue(route)!;
            Equal(request.Waypoints.Length, storedPoints.Length);
            for (int i = 0; i < storedPoints.Length; i++)
            {
                Equal(BitConverter.SingleToInt32Bits(request.Waypoints[i].X), BitConverter.SingleToInt32Bits(storedPoints[i].X));
                Equal(BitConverter.SingleToInt32Bits(request.Waypoints[i].Y), BitConverter.SingleToInt32Bits(storedPoints[i].Y));
            }
            Equal((double)request.Waypoints[0].X, (double)routeType.GetField("StartNavX")!.GetValue(route)!);
            Equal((double)request.Waypoints[0].Y, (double)routeType.GetField("StartNavY")!.GetValue(route)!);
            Equal(123456, (int)routeType.GetField("StartWorldX")!.GetValue(route)!);
            Equal(-654321, (int)routeType.GetField("StartWorldZ")!.GetValue(route)!);

            var toggle = NavigationToggle(request.RequestId + 1, request.ObjectId, true, request.UserId);
            channel.WriteInbound(Unpooled.WrappedBuffer(toggle));
            IByteBuffer toggleAck = ReadOutboundBuffer(channel);
            CheckToggleAck(toggleAck, request.RequestId + 1, request.ObjectId, true, request.UserId);
            toggleAck.Release();
            True(controller.HasRoute(request.ObjectId));
            int next = (int)routeType.GetField("Next")!.GetValue(route)!;
            Equal(1, next);
            Equal(123456, ship.globalPosition.X); Equal(-654321, ship.globalPosition.Z);
            ship.speed = 100; // Isolate route movement budget from acceleration.
            int startX = ship.globalPosition.X, startZ = ship.globalPosition.Z;
            (double expectedDx, double expectedDz) = FirstPolylinePrefix(storedPoints, 1280d, 40d);
            LockstepMovement.AdvanceTick(controller, new[] { ship }, static (_, _) => 0);
            True(ship.globalPosition.X != startX || ship.globalPosition.Z != startZ);
            True(Math.Abs(ship.globalPosition.X - (startX + expectedDx)) <= 1.0);
            True(Math.Abs(ship.globalPosition.Z - (startZ + expectedDz)) <= 1.0);
            Equal(100, ship.targetspeed);
            int nextAfterTick = (int)routeType.GetField("Next")!.GetValue(route)!;
            True(nextAfterTick >= 1 && nextAfterTick < storedPoints.Length);
            True(requestDigest.SequenceEqual(response.GeometryHash));

            var targetEvidence = storedPoints.Select((point, index) => new
            {
                index,
                navX = point.X,
                navY = point.Y,
                expectedWorldX = 123456 + (point.X - storedPoints[0].X) * 1280.0,
                expectedWorldZ = -654321 - (point.Y - storedPoints[0].Y) * 1280.0
            }).ToArray();
            Equal(123456d, targetEvidence[0].expectedWorldX);
            Equal(-654321d, targetEvidence[0].expectedWorldZ);
            Equal((double)storedPoints[^1].X, (double)targetEvidence[^1].navX);
            Equal((double)storedPoints[^1].Y, (double)targetEvidence[^1].navY);
            var fixtureEvidence = new
            {
                fixture = name,
                requestId = request.RequestId,
                requestDigest = Convert.ToHexString(requestDigest).ToLowerInvariant(),
                pointCount = request.Waypoints.Length,
                meshVersion = response.NavMeshVersion,
                serverOrigin = new { x = 123456, z = -654321 },
                targetWaypointIndex = nextAfterTick,
                expectedFirstTickWorld = new { x = startX + expectedDx, z = startZ + expectedDz },
                expectedGoalWorld = new { x = targetEvidence[^1].expectedWorldX, z = targetEvidence[^1].expectedWorldZ },
                accepted = response.Decision == (byte)PathDecision.Accepted,
                responseVersion = response.ProtocolVersion,
                responseRequestId = response.RequestId,
                responseCorrelation = response.Correlation,
                responseDigest = Convert.ToHexString(response.GeometryHash).ToLowerInvariant(),
                responseFile = responsePath,
                targets = targetEvidence
            };
            evidence.Add(fixtureEvidence);
        }
        finally
        {
            channel.FinishAndReleaseAll();
            if (hadShip) NWO_RegionNode.Program.moveMentTable[request.ObjectId] = priorShip!;
            else NWO_RegionNode.Program.moveMentTable.Remove(request.ObjectId);
        }
    }
    if (evidence.Count > 0)
        File.WriteAllText("C:\\Temp\\NWO_RouteV2_ServerParity.json",
            System.Text.Json.JsonSerializer.Serialize(evidence, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
}

// Test-only geometry oracle; production movement remains exercised by AdvanceTick.
static (double X, double Z) FirstPolylinePrefix(Vector2[] points, double scale, double distance)
{
    double remaining = distance, dxTotal = 0, dzTotal = 0;
    for (int i = 1; i < points.Length && remaining > 0; i++)
    {
        double dx = (points[i].X - points[i - 1].X) * scale;
        double dz = -(points[i].Y - points[i - 1].Y) * scale;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length <= 1e-9) continue;
        double step = Math.Min(remaining, length);
        dxTotal += dx / length * step;
        dzTotal += dz / length * step;
        remaining -= step;
    }
    return (dxTotal, dzTotal);
}

static byte[] RouteRequestV2(uint requestId, uint userId, uint objectId, int[] corridor, Vector2[] points)
{
    int total = checked(72 + corridor.Length * 4 + points.Length * 8);
    byte[] packet = new byte[total]; packet[0] = 6; packet[1] = 0; packet[2] = 2;
    Put32(packet, 4, (uint)total); Put32(packet, 8, requestId); Put32(packet, 12, userId); Put32(packet, 16, objectId);
    byte[] meshHash = Convert.FromHexString(AuthoritativeNavMeshProvider.TrustedVersion);
    Buffer.BlockCopy(meshHash, 0, packet, 20, meshHash.Length);
    Put32(packet, 52, (uint)BitConverter.SingleToInt32Bits(1280f));
    Put32(packet, 56, (uint)BitConverter.SingleToInt32Bits(2048f)); Put32(packet, 60, 1);
    Put32(packet, 64, (uint)corridor.Length); int p = 68;
    foreach (int polygon in corridor) { I32(packet, ref p, polygon); }
    Put32(packet, p, (uint)points.Length); p += 4;
    foreach (Vector2 point in points)
    {
        I32(packet, ref p, BitConverter.SingleToInt32Bits(point.X)); I32(packet, ref p, BitConverter.SingleToInt32Bits(point.Y));
    }
    return packet;
}

static void BadLength(uint length)
{
    var decoder = new PathFrameDecoder(); var ctx = new FakeContext();
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(Header(length)));
    Equal(0, decoder.PendingBytes); Equal(PathFrameDecoder.INITIAL_PENDING_CAPACITY, decoder.PendingCapacity);
    True(ctx.Closed);
}

static void OversizedRead()
{
    var decoder = new PathFrameDecoder(); var ctx = new FakeContext();
    byte[] bytes = new byte[PathFrameDecoder.MAX_FRAME_LENGTH + 1]; bytes[0] = 0x06; bytes[1] = 0x00;
    decoder.ChannelRead(ctx, Unpooled.WrappedBuffer(bytes));
    Equal(0, decoder.PendingBytes); Equal(PathFrameDecoder.INITIAL_PENDING_CAPACITY, decoder.PendingCapacity);
    True(ctx.Closed);
}

static void CorridorRegression()
{
    var validator = new PathValidator(new HarnessProvider());
    var request = new PathPolygonIndexRequest { PlanSequence = 1, NavMeshVersion = HarnessProvider.Version, StartPolygonIndex = 1,
        DestinationPolygonIndex = 3, PathPolygonCount = 3, PathPolygonIndices = new[] { 1, 2, 3 }, ClientComputedAtUtcMs = 1 };
    Equal((ushort)PathReasonCode.VALID, validator.Validate(request).ReasonCode);
    request.PathPolygonIndices = new[] { 1, 3 };
    request.PathPolygonCount = 2;
    Equal((ushort)PathReasonCode.PATH_DISCONTINUITY, validator.Validate(request).ReasonCode);
}

static void LongCorridorSampling()
{
    const int segmentCount = 200;
    var provider = new LongCorridorProvider();
    var validator = new PathValidator(provider);
    var path = Enumerable.Range(1, segmentCount + 1).ToArray();
    var request = new PathPolygonIndexRequest { PlanSequence = 99, NavMeshVersion = provider.GetVersion(),
        StartPolygonIndex = path[0], DestinationPolygonIndex = path[^1], PathPolygonCount = (uint)path.Length,
        PathPolygonIndices = path, ClientComputedAtUtcMs = 1 };
    var result = validator.Validate(request);
    Equal((ushort)PathReasonCode.VALID, result.ReasonCode);
    Equal((uint)segmentCount, result.TotalSegments);
    Equal((uint)PathValidator.MAX_GEOMETRY_SAMPLES, result.SampledSegments);
    Equal(PathValidator.MAX_GEOMETRY_SAMPLES, provider.SampledSegments.Count);
    Equal(PathValidator.MAX_GEOMETRY_SAMPLES, provider.SampledSegments.Distinct().Count());
}

static byte[] Request(uint id, uint userId = 7)
{
    return RequestPath(id, userId, 1, 2, 3);
}

static byte[] RouteRequest(uint id, uint userId, params int[] path)
{
    uint objectId = NWO_RegionNode.Program.userTable.TryGetValue(userId, out User user) ? user.scaffoldingIndex : 0;
    return RouteRequestForObject(id, userId, objectId, path);
}
static byte[] RouteRequestForObject(uint id, uint userId, uint objectId, params int[] path)
{
    byte[] body = new byte[12 + path.Length * 4]; int p = 0;
    U32(body, ref p, userId); U32(body, ref p, objectId); U32(body, ref p, (uint)path.Length); foreach (int polygon in path) I32(body, ref p, polygon);
    byte[] packet = new byte[12 + body.Length]; packet[0] = 6; packet[1] = 0; packet[2] = 1;
    Put32(packet, 4, (uint)packet.Length); Put32(packet, 8, id); Buffer.BlockCopy(body, 0, packet, 12, body.Length); return packet;
}
static byte[] RequestPath(uint id, uint userId, params int[] path)
{
    byte[] body = new byte[12 + path.Length * 4]; int p = 0;
    U32(body, ref p, userId); U32(body, ref p, 0); U32(body, ref p, (uint)path.Length); foreach (int polygon in path) I32(body, ref p, polygon);
    byte[] packet = new byte[12 + body.Length]; packet[0] = 6; packet[1] = 0; packet[2] = 1;
    Put32(packet, 4, (uint)packet.Length); Put32(packet, 8, id); Buffer.BlockCopy(body, 0, packet, 12, body.Length); return packet;
}
static byte[] Header(uint length) { var h = new byte[8]; h[0] = 6; Put32(h, 4, length); return h; }
static void U16(byte[] b, ref int p, ushort n) { b[p++] = (byte)n; b[p++] = (byte)(n >> 8); }
static void U32(byte[] b, ref int p, uint n) { Put32(b, p, n); p += 4; }
static void I32(byte[] b, ref int p, int n) => U32(b, ref p, (uint)n);
static void U64(byte[] b, ref int p, ulong n) { for (int i = 0; i < 8; i++) b[p++] = (byte)(n >> (8 * i)); }
static void Put32(byte[] b, int p, uint n) { for (int i = 0; i < 4; i++) b[p + i] = (byte)(n >> (8 * i)); }
static uint Get32(byte[] b, int p) => (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);
static (byte Opcode, byte Subtype, ushort ProtocolVersion, uint DeclaredLength, int RawLength, uint RequestId, ulong Correlation, byte Decision, ushort Reason, string NavMeshVersion, uint Sampled, uint Total, string ServerPlanId, byte[] GeometryHash) ParseResponse(IByteBuffer b)
{
    try {
        int i = b.ReaderIndex, end = i + b.ReadableBytes; int p = i;
        byte op = unchecked((byte)b.GetByte(p++)), sub = unchecked((byte)b.GetByte(p++)); ushort version = b.GetUnsignedShortLE(p); p += 2;
        uint declared = b.GetUnsignedIntLE(p); p += 4; uint request = b.GetUnsignedIntLE(p); p += 4;
        if (declared != b.ReadableBytes || declared > int.MaxValue) throw new Exception("Response frame length mismatch");
        ulong correlation = b.GetUnsignedIntLE(p) | ((ulong)b.GetUnsignedIntLE(p + 4) << 32); p += 8; byte decision = unchecked((byte)b.GetByte(p++)); ushort reason = b.GetUnsignedShortLE(p); p += 2;
        string ReadString() { ushort n = b.GetUnsignedShortLE(p); p += 2; if (p + n > end) throw new Exception("Truncated response string"); byte[] bytes = new byte[n]; b.GetBytes(p, bytes, 0, n); string s = Encoding.UTF8.GetString(bytes); p += n; return s; }
        string nav = ReadString(); uint sampled = b.GetUnsignedIntLE(p); p += 4; uint total = b.GetUnsignedIntLE(p); p += 4; string plan = ReadString();
        byte[] geometryHash = Array.Empty<byte>();
        if (version == 2)
        {
            if (p + 32 != end) throw new Exception("V2 response digest length mismatch");
            geometryHash = new byte[32]; b.GetBytes(p, geometryHash, 0, 32); p += 32;
        }
        if (p != end) throw new Exception("Trailing response bytes");
        return (op, sub, version, declared, b.ReadableBytes, request, correlation, decision, reason, nav, sampled, total, plan, geometryHash);
    }
    finally { b.Release(); }
}
static void True(bool value) { if (!value) throw new Exception("Expected true"); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; got {actual}");
}

static void DatelineCorridorRegression()
{
    var provider = new DatelineHarnessProvider();
    var validator = new PathValidator(provider);
    var request = new PathPolygonIndexRequest { StartPolygonIndex = 1, DestinationPolygonIndex = 2,
        PathPolygonCount = 2, PathPolygonIndices = new[] { 1, 2 }, ProtocolVersion = 2,
        NavMeshVersion = provider.GetVersion(), Waypoints = new[] { new Vector2(10, 10), new Vector2(11, 10) } };
    Equal((ushort)PathReasonCode.VALID, validator.Validate(request).ReasonCode);
    request.ProtocolVersion = 1;
    Equal((ushort)PathReasonCode.PATH_DISCONTINUITY, validator.Validate(request).ReasonCode);
    request.ProtocolVersion = 2; request.Waypoints = new[] { new Vector2(12, 10), new Vector2(13, 10) };
    Equal((ushort)PathReasonCode.OFF_MESH_SAMPLE, validator.Validate(request).ReasonCode);
}

static void LandMaskPeriodicLookup()
{
    var mask = AuthoritativeLandMask.TryLoad(Path.Combine(AppContext.BaseDirectory, "data", "server-landmask.bytes"));
    True(mask != null);
    True(mask!.TryGetBit(new Vector2(-0.0001f, 100), out bool negative));
    True(mask.TryGetBit(new Vector2(2048, 100), out bool zero));
    True(mask.TryGetBit(new Vector2(0, 100), out bool zeroAgain));
    Equal(zero, zeroAgain);
    // Tiny negative x maps to the last chart column, whose packaged bits are water at this row.
    True(!negative);
    True(!mask.TryGetBit(new Vector2(0, float.NaN), out _));
    True(!mask.TryGetBit(new Vector2(0, 0), out _));
}

class HarnessProvider : IAuthoritativeNavMeshProvider
{
    public const string Version = AuthoritativeNavMeshProvider.TrustedVersion;
    public string GetVersion() => Version;
    public bool HasPolygon(int p) => p is >= 1 and <= 3;
    public bool IsOnNavMesh(int p) => HasPolygon(p);
    public virtual bool TryGetCentroid(int p, out double x, out double y) { x = y = 0; return false; }
    public bool CanMoveBetween(int a, int b) => (a, b) is (1, 2) or (2, 1) or (2, 3) or (3, 2);
    public virtual bool ValidateGeometry(int[] path, out uint sampled, out uint total, out PathReasonCode reason)
    { sampled = total = (uint)(path.Length - 1); reason = PathReasonCode.VALID; return true; }
}

sealed class RangeProvider : IAuthoritativeNavMeshProvider
{
    private readonly int _max;
    public RangeProvider(int max) { _max = max; }
    public string GetVersion() => HarnessProvider.Version;
    public bool HasPolygon(int p) => p >= 0 && p <= _max;
    public bool IsOnNavMesh(int p) => HasPolygon(p);
    public bool CanMoveBetween(int a, int b) => Math.Abs(a - b) == 1;
    public bool ValidateGeometry(int[] path, out uint sampled, out uint total, out PathReasonCode reason)
    { sampled = total = 1; reason = PathReasonCode.VALID; return true; }
}

sealed class FailingGeometryProvider : HarnessProvider
{
    public override bool ValidateGeometry(int[] path, out uint sampled, out uint total, out PathReasonCode reason)
    { sampled = 1; total = 1; reason = PathReasonCode.OFF_MESH_SAMPLE; return false; }
}

sealed class LongCorridorProvider : IAuthoritativeNavMeshProvider
{
    public HashSet<(int From, int To)> SampledSegments { get; } = new();
    public string GetVersion() => HarnessProvider.Version;
    public bool HasPolygon(int p) => p > 0;
    public bool IsOnNavMesh(int p) => p > 0;
    public bool CanMoveBetween(int a, int b) => Math.Abs(a - b) == 1;
    public bool ValidateGeometry(int[] path, out uint sampled, out uint total, out PathReasonCode reason)
    { SampledSegments.Add((path[0], path[1])); sampled = total = 1; reason = PathReasonCode.VALID; return true; }
}

class CentroidProvider : HarnessProvider
{
    public override bool TryGetCentroid(int polygon, out double x, out double y) { x = polygon == 1 ? 0 : 10; y = 0; return polygon is 1 or 2; }
}

sealed class DatelineHarnessProvider : IAuthoritativeNavMeshProvider
{
    public string GetVersion() => HarnessProvider.Version;
    public bool HasPolygon(int p) => p is 1 or 2;
    public bool IsOnNavMesh(int p) => HasPolygon(p);
    public bool CanMoveBetween(int a, int b) => false;
    public bool CanMoveAcrossDateline(int a, int b) => (a, b) is (1, 2) or (2, 1);
    public bool TryGetCentroid(int p, out double x, out double y) { x = y = 10; return HasPolygon(p); }
    public bool ValidateGeometry(int[] path, out uint sampled, out uint total, out PathReasonCode reason)
    { sampled = total = 1; reason = PathReasonCode.VALID; return true; }
    bool IAuthoritativeNavMeshProvider.ValidateWaypointRoute(int start, int goal, Vector2[] points, out PathReasonCode reason)
    { reason = points[0].X == 10 ? PathReasonCode.VALID : PathReasonCode.OFF_MESH_SAMPLE; return reason == PathReasonCode.VALID; }
}

sealed class V2GeometryProvider : CentroidProvider, IAuthoritativeNavMeshProvider
{
    public bool SawNonCentroidWaypoint;
    bool IAuthoritativeNavMeshProvider.ValidateWaypointRoute(int startPolygon, int goalPolygon, Vector2[] points, out PathReasonCode reason)
    {
        // The submitted LOS point differs from either polygon centroid.
        SawNonCentroidWaypoint = points.Length > 1 && points[1].X != 110f && points[1].Y != 100f;
        reason = startPolygon == 1 && goalPolygon == 2 ? PathReasonCode.VALID : PathReasonCode.PLAN_INVALID;
        return reason == PathReasonCode.VALID;
    }
    public override bool TryGetCentroid(int polygon, out double x, out double y)
    { x = polygon == 1 ? 100 : 110; y = 100; return polygon is 1 or 2; }
}

sealed class AdjustableCentroidProvider : CentroidProvider
{
    public double DestinationX = 10;
    public override bool TryGetCentroid(int polygon, out double x, out double y)
    { x = polygon == 1 ? 0 : DestinationX; y = 0; return polygon is 1 or 2; }
}

sealed class FakeContext : IChannelHandlerContext
{
    public FakeContext() : this(new TestChannelId(Guid.NewGuid().ToString("N"))) { }
    public FakeContext(IChannelId id) { Channel = new DotNetty.Transport.Channels.Embedded.EmbeddedChannel(id); }
    public List<object> Reads { get; } = new(); public List<IByteBuffer> Writes { get; } = new(); public bool Closed;
    public IByteBuffer TakeOne() { var item = Writes[0]; Writes.RemoveAt(0); return item; }
    public Task WriteAndFlushAsync(object message) { Writes.Add((IByteBuffer)message); return Task.CompletedTask; }
    public Task CloseAsync() { Closed = true; return Task.CompletedTask; }
    public IChannelHandlerContext FireChannelRead(object message) { Reads.Add(message); return this; }
    public IChannel Channel { get; }
    public IChannelPipeline Pipeline => throw new NotImplementedException();
    public IByteBufferAllocator Allocator => UnpooledByteBufferAllocator.Default;
    public string Name => "harness";
    public IChannelHandler Handler => throw new NotImplementedException();
    public DotNetty.Common.Concurrency.IEventExecutor Executor => throw new NotImplementedException();
    public bool Removed => false;
    public DotNetty.Common.Utilities.IAttribute<T> GetAttribute<T>(DotNetty.Common.Utilities.AttributeKey<T> key) where T : class => throw new NotImplementedException();
    public bool HasAttribute<T>(DotNetty.Common.Utilities.AttributeKey<T> key) where T : class => false;
    public IChannelHandlerContext FireChannelRegistered() => this; public IChannelHandlerContext FireChannelUnregistered() => this;
    public IChannelHandlerContext FireChannelActive() => this; public IChannelHandlerContext FireChannelInactive() => this;
    public IChannelHandlerContext FireExceptionCaught(Exception cause) => this; public IChannelHandlerContext FireUserEventTriggered(object evt) => this;
    public IChannelHandlerContext FireChannelReadComplete() => this; public IChannelHandlerContext FireChannelWritabilityChanged() => this;
    public IChannelHandlerContext FireChannelRead(object msg, bool singleDecode) => FireChannelRead(msg);
    public IChannelHandlerContext FireChannelReadComplete(bool singleDecode) => this;
    public IChannelHandlerContext Read() => this; public IChannelHandlerContext Flush() => this;
    public Task BindAsync(System.Net.EndPoint localAddress) => throw new NotImplementedException();
    public Task ConnectAsync(System.Net.EndPoint remoteAddress) => throw new NotImplementedException();
    public Task ConnectAsync(System.Net.EndPoint remoteAddress, System.Net.EndPoint localAddress) => throw new NotImplementedException();
    public Task DisconnectAsync() => throw new NotImplementedException(); public Task CloseAsync(bool flush) => CloseAsync();
    public Task DeregisterAsync() => throw new NotImplementedException(); public Task DeregisterAsync(bool flush) => throw new NotImplementedException();
    public Task BindAsync(System.Net.EndPoint localAddress, bool flush) => throw new NotImplementedException();
    public Task ConnectAsync(System.Net.EndPoint remoteAddress, bool flush) => throw new NotImplementedException();
    public Task DisconnectAsync(bool flush) => throw new NotImplementedException();
    public Task WriteAsync(object message) => throw new NotImplementedException(); public Task WriteAsync(object message, bool flush) => throw new NotImplementedException();
    public Task WriteAndFlushAsync(object message, bool flush) => WriteAndFlushAsync(message);
    public Task WriteAndFlushAsync(object message, System.Threading.CancellationToken cancellationToken) => WriteAndFlushAsync(message);
    public Task WriteAsync(object message, System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task CloseAsync(System.Threading.CancellationToken cancellationToken) => CloseAsync();
    public Task BindAsync(System.Net.EndPoint localAddress, System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task ConnectAsync(System.Net.EndPoint remoteAddress, System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task DisconnectAsync(System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task DeregisterAsync(System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task WriteAsync(object message, bool flush, System.Threading.CancellationToken cancellationToken) => throw new NotImplementedException();
}

sealed class ChannelContext : IChannelHandlerContext
{
    private readonly DotNetty.Transport.Channels.Embedded.EmbeddedChannel _channel;
    public ChannelContext(DotNetty.Transport.Channels.Embedded.EmbeddedChannel channel) { _channel = channel; }
    public IChannel Channel => _channel;
    public Task WriteAndFlushAsync(object message) => _channel.WriteAndFlushAsync(message);
    public Task CloseAsync() => _channel.CloseAsync();
    public IChannelPipeline Pipeline => _channel.Pipeline;
    public DotNetty.Buffers.IByteBufferAllocator Allocator => _channel.Allocator;
    public string Name => "embedded-owner";
    public IChannelHandler Handler => throw new NotImplementedException();
    public DotNetty.Common.Concurrency.IEventExecutor Executor => _channel.EventLoop;
    public bool Removed => false;
    public DotNetty.Common.Utilities.IAttribute<T> GetAttribute<T>(DotNetty.Common.Utilities.AttributeKey<T> key) where T : class => _channel.GetAttribute(key);
    public bool HasAttribute<T>(DotNetty.Common.Utilities.AttributeKey<T> key) where T : class => _channel.HasAttribute(key);
    public IChannelHandlerContext FireChannelRegistered() => this;
    public IChannelHandlerContext FireChannelUnregistered() => this;
    public IChannelHandlerContext FireChannelActive() => this;
    public IChannelHandlerContext FireChannelInactive() => this;
    public IChannelHandlerContext FireExceptionCaught(Exception cause) => this;
    public IChannelHandlerContext FireUserEventTriggered(object evt) => this;
    public IChannelHandlerContext FireChannelRead(object message) => this;
    public IChannelHandlerContext FireChannelRead(object message, bool singleDecode) => this;
    public IChannelHandlerContext FireChannelReadComplete() => this;
    public IChannelHandlerContext FireChannelReadComplete(bool singleDecode) => this;
    public IChannelHandlerContext FireChannelWritabilityChanged() => this;
    public IChannelHandlerContext Read() => this;
    public IChannelHandlerContext Flush() => this;
    public Task BindAsync(System.Net.EndPoint localAddress) => _channel.BindAsync(localAddress);
    public Task ConnectAsync(System.Net.EndPoint remoteAddress) => _channel.ConnectAsync(remoteAddress);
    public Task ConnectAsync(System.Net.EndPoint remoteAddress, System.Net.EndPoint localAddress) => _channel.ConnectAsync(remoteAddress, localAddress);
    public Task DisconnectAsync() => _channel.DisconnectAsync();
    public Task CloseAsync(bool flush) => CloseAsync();
    public Task DeregisterAsync() => _channel.DeregisterAsync();
    public Task DeregisterAsync(bool flush) => DeregisterAsync();
    public Task BindAsync(System.Net.EndPoint localAddress, bool flush) => BindAsync(localAddress);
    public Task ConnectAsync(System.Net.EndPoint remoteAddress, bool flush) => ConnectAsync(remoteAddress);
    public Task DisconnectAsync(bool flush) => DisconnectAsync();
    public Task WriteAsync(object message) => _channel.WriteAsync(message);
    public Task WriteAsync(object message, bool flush) => WriteAsync(message);
    public Task WriteAndFlushAsync(object message, bool flush) => WriteAndFlushAsync(message);
    public Task WriteAndFlushAsync(object message, System.Threading.CancellationToken cancellationToken) => WriteAndFlushAsync(message);
    public Task WriteAsync(object message, System.Threading.CancellationToken cancellationToken) => WriteAsync(message);
    public Task CloseAsync(System.Threading.CancellationToken cancellationToken) => CloseAsync();
    public Task BindAsync(System.Net.EndPoint localAddress, System.Threading.CancellationToken cancellationToken) => BindAsync(localAddress);
    public Task ConnectAsync(System.Net.EndPoint remoteAddress, System.Threading.CancellationToken cancellationToken) => ConnectAsync(remoteAddress);
    public Task DisconnectAsync(System.Threading.CancellationToken cancellationToken) => DisconnectAsync();
    public Task DeregisterAsync(System.Threading.CancellationToken cancellationToken) => DeregisterAsync();
    public Task WriteAsync(object message, bool flush, System.Threading.CancellationToken cancellationToken) => WriteAsync(message, flush);
}

sealed class TestChannelId : IChannelId
{
    private readonly string _id;
    public TestChannelId(string id) { _id = id; }
    public string AsShortText() => _id;
    public string AsLongText() => _id;
    public int CompareTo(IChannelId? other) => string.CompareOrdinal(_id, other?.AsLongText());
    public override string ToString() => _id;
    public override int GetHashCode() => _id.GetHashCode(StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is IChannelId id && _id == id.AsLongText();
}
