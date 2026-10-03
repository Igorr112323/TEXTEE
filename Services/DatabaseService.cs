using System.Globalization;
using Microsoft.Data.Sqlite;
using Visits11.Models;

namespace Visits11.Services;

/// <summary>
/// Локальная SQLite-база (attendance.db рядом с exe).
/// </summary>
public sealed class DatabaseService
{
    private readonly string _connectionString;

    /// <summary>Вызывается после любого изменения данных (группы/студенты/занятия).</summary>
    public event Action? DataChanged;

    public DatabaseService(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Groups (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Name TEXT NOT NULL UNIQUE,
              CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Students (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              GroupId INTEGER NOT NULL,
              FullName TEXT NOT NULL,
              Login TEXT NOT NULL UNIQUE,
              Password TEXT NOT NULL,
              PhoneId TEXT NULL,
              DeviceId TEXT NULL,
              CreatedAt TEXT NULL,
              FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Lessons (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              GroupId INTEGER NOT NULL,
              StartedAt TEXT NOT NULL,
              FinishedAt TEXT NULL,
              FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Attendance (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              LessonId INTEGER NOT NULL,
              StudentId INTEGER NOT NULL,
              Status TEXT NOT NULL,
              MarkedAt TEXT NOT NULL,
              FOREIGN KEY (LessonId) REFERENCES Lessons(Id) ON DELETE CASCADE,
              FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Sessions (
              Token TEXT PRIMARY KEY,
              StudentId INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // привязка аккаунта к устройству (для старых баз — добавляем колонку)
        try
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Students ADD COLUMN DeviceId TEXT NULL";
            alter.ExecuteNonQuery();
        }
        catch
        {
            // колонка уже есть
        }

        try
        {
            using var created = connection.CreateCommand();
            created.CommandText = "ALTER TABLE Students ADD COLUMN CreatedAt TEXT NULL";
            created.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    /// <summary>Устройство, к которому привязан аккаунт студента (null — ещё не привязан).</summary>
    public string? GetDeviceId(int studentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId FROM Students WHERE Id = @id";
        command.Parameters.AddWithValue("@id", studentId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Привязывает аккаунт студента к устройству.</summary>
    public void SetDeviceId(int studentId, string deviceId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Students SET DeviceId = @device WHERE Id = @id";
        command.Parameters.AddWithValue("@device", deviceId);
        command.Parameters.AddWithValue("@id", studentId);
        command.ExecuteNonQuery();
    }

    /// <summary>Студент, к чьему аккаунту привязано устройство; null — устройство неизвестно.</summary>
    public int? FindStudentIdByDevice(string device)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Students WHERE DeviceId = @device LIMIT 1";
        command.Parameters.AddWithValue("@device", device);
        return command.ExecuteScalar() is long value ? (int)value : null;
    }

    /// <summary>Удаляет студента вместе с его отметками и сессией телефона.</summary>
    public void DeleteStudent(int studentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE StudentId = @id; DELETE FROM Students WHERE Id = @id";
        command.Parameters.AddWithValue("@id", studentId);
        command.ExecuteNonQuery();
        RaiseDataChanged();
    }

    /// <summary>Удаляет группу вместе со студентами, занятиями и отметками.</summary>
    public void DeleteGroup(int groupId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM Sessions WHERE StudentId IN (SELECT Id FROM Students WHERE GroupId = @id); DELETE FROM Groups WHERE Id = @id";
        command.Parameters.AddWithValue("@id", groupId);
        command.ExecuteNonQuery();
        RaiseDataChanged();
    }

    /// <summary>Имя студента по id (для подсказок).</summary>
    public string? GetStudentName(int studentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FullName FROM Students WHERE Id = @id";
        command.Parameters.AddWithValue("@id", studentId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Токены сессий студентов — переживают перезапуск приложения.</summary>
    public Dictionary<string, int> LoadSessions()
    {
        var result = new Dictionary<string, int>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Token, StudentId FROM Sessions";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }
        return result;
    }

    /// <summary>Сохраняет выданный токен сессии.</summary>
    public void SaveSession(string token, int studentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO Sessions (Token, StudentId) VALUES (@token, @studentId)";
        command.Parameters.AddWithValue("@token", token);
        command.Parameters.AddWithValue("@studentId", studentId);
        command.ExecuteNonQuery();
    }

    private void RaiseDataChanged() => DataChanged?.Invoke();

    // ------------------------------------------------------------------ группы

    public List<Group> GetGroups()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, CreatedAt FROM Groups ORDER BY CreatedAt, Id";
        using var reader = command.ExecuteReader();
        var list = new List<Group>();
        while (reader.Read())
        {
            list.Add(new Group
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CreatedAt = reader.GetString(2),
            });
        }
        return list;
    }

    public bool GroupNameExists(string name)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Groups WHERE Name = @name";
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public Group AddGroup(string name)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Groups (Name, CreatedAt) VALUES (@name, @createdAt); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        var id = Convert.ToInt32(command.ExecuteScalar());
        RaiseDataChanged();
        return new Group { Id = id, Name = name, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
    }

    public int CountStudents(int groupId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Students WHERE GroupId = @id";
        command.Parameters.AddWithValue("@id", groupId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // ---------------------------------------------------------------- студенты

    public List<Student> GetStudents(int groupId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, GroupId, FullName, Login, Password, PhoneId, DeviceId FROM Students WHERE GroupId = @id ORDER BY Id";
        command.Parameters.AddWithValue("@id", groupId);
        using var reader = command.ExecuteReader();
        var list = new List<Student>();
        while (reader.Read())
        {
            list.Add(new Student
            {
                Id = reader.GetInt32(0),
                GroupId = reader.GetInt32(1),
                FullName = reader.GetString(2),
                Login = reader.GetString(3),
                Password = reader.GetString(4),
                PhoneId = reader.IsDBNull(5) ? null : reader.GetString(5),
                DeviceId = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }
        return list;
    }

    /// <summary>Студент по логину (для входа в приложении студента) или null.</summary>
    public Student? FindByLogin(string login)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, GroupId, FullName, Login, Password, PhoneId FROM Students WHERE Login = @login COLLATE NOCASE LIMIT 1";
        command.Parameters.AddWithValue("@login", login);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Student
        {
            Id = reader.GetInt32(0),
            GroupId = reader.GetInt32(1),
            FullName = reader.GetString(2),
            Login = reader.GetString(3),
            Password = reader.GetString(4),
            PhoneId = reader.IsDBNull(5) ? null : reader.GetString(5),
        };
    }

    public bool LoginExists(string login)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Students WHERE Login = @login COLLATE NOCASE";
        command.Parameters.AddWithValue("@login", login);        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public void AddStudent(Student student)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Students (GroupId, FullName, Login, Password, PhoneId, CreatedAt)
            VALUES (@groupId, @fullName, @login, @password, @phoneId, @createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@groupId", student.GroupId);
        command.Parameters.AddWithValue("@fullName", student.FullName);
        command.Parameters.AddWithValue("@login", student.Login);
        command.Parameters.AddWithValue("@password", student.Password);
        command.Parameters.AddWithValue("@phoneId", (object?)student.PhoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        student.Id = Convert.ToInt32(command.ExecuteScalar());
        RaiseDataChanged();
    }

    public void AddStudents(IEnumerable<Student> students)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Students (GroupId, FullName, Login, Password, PhoneId, CreatedAt)
            VALUES (@groupId, @fullName, @login, @password, @phoneId, @createdAt);
            """;
        var pGroupId = command.Parameters.Add("@groupId", SqliteType.Integer);
        var pFullName = command.Parameters.Add("@fullName", SqliteType.Text);
        var pLogin = command.Parameters.Add("@login", SqliteType.Text);
        var pPassword = command.Parameters.Add("@password", SqliteType.Text);
        var pPhoneId = command.Parameters.Add("@phoneId", SqliteType.Text);
        var pCreatedAt = command.Parameters.Add("@createdAt", SqliteType.Text);
        var createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var student in students)
        {
            pGroupId.Value = student.GroupId;
            pFullName.Value = student.FullName;
            pLogin.Value = student.Login;
            pPassword.Value = student.Password;
            pPhoneId.Value = (object?)student.PhoneId ?? DBNull.Value;
            pCreatedAt.Value = createdAt;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        RaiseDataChanged();
    }

    public void UpdateStudentPhoneId(int studentId, string phoneId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Students SET PhoneId = @phoneId WHERE Id = @id";
        command.Parameters.AddWithValue("@phoneId", phoneId);
        command.Parameters.AddWithValue("@id", studentId);
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- занятия

    public int AddLesson(int groupId, string startedAt, string finishedAt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Lessons (GroupId, StartedAt, FinishedAt)
            VALUES (@groupId, @startedAt, @finishedAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@groupId", groupId);
        command.Parameters.AddWithValue("@startedAt", startedAt);
        command.Parameters.AddWithValue("@finishedAt", finishedAt);
        var id = Convert.ToInt32(command.ExecuteScalar());
        RaiseDataChanged();
        return id;
    }

    public List<Lesson> GetLessons(int groupId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.Id, l.GroupId, l.StartedAt, l.FinishedAt,
              (SELECT COUNT(*) FROM Attendance a WHERE a.LessonId = l.Id AND a.Status = 'present') AS Present,
              (SELECT COUNT(*) FROM Students s WHERE s.GroupId = l.GroupId) AS Total
            FROM Lessons l
            WHERE l.GroupId = @id
            ORDER BY l.StartedAt DESC, l.Id DESC
            """;
        command.Parameters.AddWithValue("@id", groupId);
        using var reader = command.ExecuteReader();
        var list = new List<Lesson>();
        while (reader.Read())
        {
            list.Add(new Lesson
            {
                Id = reader.GetInt32(0),
                GroupId = reader.GetInt32(1),
                StartedAt = reader.GetString(2),
                FinishedAt = reader.IsDBNull(3) ? null : reader.GetString(3),
                Present = Convert.ToInt32(reader.GetInt64(4)),
                Total = Convert.ToInt32(reader.GetInt64(5)),
            });
        }
        return list;
    }

    public List<LessonDetail> GetLessonDetails(int lessonId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.FullName, a.Status
            FROM Students s
            LEFT JOIN Attendance a ON a.StudentId = s.Id AND a.LessonId = @lessonId
            WHERE s.GroupId = (SELECT GroupId FROM Lessons WHERE Id = @lessonId)
            ORDER BY s.Id
            """;
        command.Parameters.AddWithValue("@lessonId", lessonId);
        using var reader = command.ExecuteReader();
        var list = new List<LessonDetail>();
        while (reader.Read())
        {
            list.Add(new LessonDetail
            {
                StudentId = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Present = !reader.IsDBNull(2) && reader.GetString(2) == "present",
            });
        }
        return list;
    }

    public void AddAttendance(int lessonId, IEnumerable<(int StudentId, bool Present, string MarkedAt)> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Attendance (LessonId, StudentId, Status, MarkedAt)
            VALUES (@lessonId, @studentId, @status, @markedAt);
            """;
        var pLessonId = command.Parameters.Add("@lessonId", SqliteType.Integer);
        var pStudentId = command.Parameters.Add("@studentId", SqliteType.Integer);
        var pStatus = command.Parameters.Add("@status", SqliteType.Text);
        var pMarkedAt = command.Parameters.Add("@markedAt", SqliteType.Text);

        foreach (var (studentId, present, markedAt) in rows)
        {
            pLessonId.Value = lessonId;
            pStudentId.Value = studentId;
            pStatus.Value = present ? "present" : "absent";
            pMarkedAt.Value = markedAt;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public SemesterReport BuildSemester(int groupId, DateTime from)
    {
        var fromText = from.ToString("yyyy-MM-dd");
        using var connection = Open();
        var lessons = new List<(int Id, string Started)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, StartedAt FROM Lessons
                WHERE GroupId = @id AND StartedAt >= @from
                ORDER BY StartedAt, Id
                """;
            command.Parameters.AddWithValue("@id", groupId);
            command.Parameters.AddWithValue("@from", fromText);
            using var reader = command.ExecuteReader();
            while (reader.Read()) lessons.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        var students = new List<(int Id, string Name, string? Created)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, FullName, CreatedAt FROM Students WHERE GroupId = @id ORDER BY Id";
            command.Parameters.AddWithValue("@id", groupId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                students.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var present = new HashSet<(int StudentId, int LessonId)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.StudentId, a.LessonId
                FROM Attendance a
                JOIN Lessons l ON l.Id = a.LessonId
                WHERE l.GroupId = @id AND l.StartedAt >= @from AND a.Status = 'present'
                """;
            command.Parameters.AddWithValue("@id", groupId);
            command.Parameters.AddWithValue("@from", fromText);
            using var reader = command.ExecuteReader();
            while (reader.Read()) present.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        var headers = new List<string>();
        var seen = new Dictionary<string, int>();
        foreach (var lesson in lessons)
        {
            var label = DateTime.TryParseExact(lesson.Started, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("dd.MM")
                : lesson.Started;
            if (!seen.TryAdd(label, 1))
            {
                var count = seen[label] + 1;
                seen[label] = count;
                label = $"{label} {count}";
            }
            headers.Add(label);
        }

        var report = new SemesterReport { Headers = headers };
        foreach (var student in students)
        {
            var marks = new List<string>();
            var counted = 0;
            var was = 0;
            foreach (var lesson in lessons)
            {
                if (!Counts(student.Created, lesson.Started))
                {
                    marks.Add("—");
                    continue;
                }
                counted++;
                if (present.Contains((student.Id, lesson.Id)))
                {
                    was++;
                    marks.Add("+");
                }
                else marks.Add("н");
            }
            report.Students.Add(new SemesterStudent
            {
                Name = student.Name,
                Marks = marks,
                TotalText = $"{was}/{counted}",
                PercentText = counted == 0 ? "—" : $"{(int)Math.Round(was * 100.0 / counted)}%",
            });
        }
        return report;
    }

    private static bool Counts(string? createdAt, string startedRaw)
    {
        if (string.IsNullOrEmpty(createdAt)) return true;
        if (!DateTime.TryParseExact(createdAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
            return true;
        if (!DateTime.TryParseExact(startedRaw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lessonStart))
            return true;
        return created <= lessonStart;
    }
}
