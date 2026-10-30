//! Convert LCSC/EasyEDA components to Altium SchLib / PcbLib.

pub mod cli;
pub mod convert;
pub mod easyeda;
pub mod error;
pub mod local_index;
pub mod local_catalog;

pub use error::{Error, Result};

pub mod slot_repair;
pub mod source_cache;
