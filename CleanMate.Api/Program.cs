using CleanMate.Api;
using CleanMate.Api.Extensions;

var app = ApiApplication.Build(args);
app.ApplyCleanMateMigrations();
app.Run();
