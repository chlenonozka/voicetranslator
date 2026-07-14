import re

with open('tests/VoiceTranslator.UnitTests/Services/DesktopRuntimeServiceTests.cs', 'r') as f:
    content = f.read()

content = content.replace("service.SessionFactory = new FakeSessionFactory(session);", "service.SessionFactory = new FakeSessionFactory(session);\n        service.DispatcherFunc = action => { action(); return Task.CompletedTask; };")

with open('tests/VoiceTranslator.UnitTests/Services/DesktopRuntimeServiceTests.cs', 'w') as f:
    f.write(content)
