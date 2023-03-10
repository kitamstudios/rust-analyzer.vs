#[cfg(test)]
mod tests1;

use std;
use add_one;

fn main() {
    let num = 10;
    println!("Hello, world! {num} plus one is {}!", add_one::add_one(num));

    println!("A B = {}", std::env::var_os("A B").unwrap_or(std::ffi::OsString::from("NOT FOUND!")).to_str().unwrap());
    println!("XX = {}", std::env::var("XX").unwrap_or("NOT FOUND!".to_string()));
    println!("A = {}", std::env::var("A").unwrap_or("NOT FOUND!".to_string()));

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}

#[cfg(test)]
mod tests {
    use super::*;
    use add_one::*;

    #[test]
    fn it_works_passing() {
        assert_eq!(3, add_one(2));
    }

    #[test]
    fn it_works_failing() {
        assert_eq!(3, add_one(4));
    }

    #[test]
    #[ignore = "this is a test for ignore aka skip"]
    fn it_works_skipped() {
        assert_eq!(3, add_one(2));
    }
}
