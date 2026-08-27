using AIMS.Core.Events;
using AIMS.SharedKernel;
using System;
using System.Collections.Generic;
using System.Text;

namespace AIMS.Core.Entities;

public class ToDoItem : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; }
    public bool IsDone { get; private set; }

    public void MarkComplete()
    {
        IsDone = true;
        Events.Add(new ToDoItemCompletedEvent(this));
    }
}
